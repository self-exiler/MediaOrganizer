using System.Collections.Concurrent;
using MediaOrganizer.Core.Diagnostics;
using MediaOrganizer.Core.Sources;
using SMBLibrary;
using SMBLibrary.Client;
using FileAttributes = SMBLibrary.FileAttributes;

namespace MediaOrganizer.Core.Storage;

/// <summary>
/// SMB 存储输出目标（ADR-0007）：基于 SMBLibrary 纯托管客户端（SMB2，445 端口），双端统一实现，
/// 替代旧 UNC/OS 重定向器方式——连接配置的用户名/密码真正生效。
/// 限制：首版不支持 guest/匿名；SMBLibrary 不支持 SMB 3.1.1 强制加密的服务器（主流家用 NAS 默认不强制）。
/// 连接在首次操作时懒建立并在存储生命周期内复用（连接 + NTLM 登录开销大）；
/// SMBLibrary 客户端非线程安全，除 CopyFromAsync 的写入环节按块持锁外，其余操作经 _gate 整体串行化。
/// 写入按 min(协商 MaxWriteSize, 1MB) 分块（SMBLibrary 的 WriteFile 不自动分片，超限必失败）；
/// .mo-tmp 临时名 + 写入自证大小校验、重试退避由 FileOperator 层统一保证。
/// 提速（评估文档 §5）：目录创建缓存 + 目录列表缓存（ExistsAsync 免往返）+ 读写流水线（预读与写重叠）。
/// </summary>
public sealed class SmbFileStorage : IFileStorage
{
    /// <summary>
    /// 单次 SMB2 Write 的硬上限。SMBLibrary 的 WriteFile 不会按协商值自动分片，
    /// 超过 MaxWriteSize 的写请求会被服务器直接拒绝（STATUS_INVALID_PARAMETER）。
    /// </summary>
    private const int MaxWriteChunkSize = 1024 * 1024;

    /// <summary>协商值不可用时的保守写入块大小（SMB 2.0.2 常见上限）。</summary>
    private const int FallbackWriteChunkSize = 64 * 1024;

    private readonly string _server;
    private readonly string _share;
    private readonly string _baseSmbPath; // share 内基础路径（'\' 分隔，无首尾分隔符）
    private readonly string _username;
    private readonly string _password;

    private SMB2Client? _client;
    private ISMBFileStore? _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // 目录创建缓存（实例生命周期 = 一次执行，OrganizeSession 每次执行新建实例，天然隔离）
    private readonly ConcurrentDictionary<string, bool> _knownDirs = new();

    // 目录列表缓存：父目录 SMB 路径 → (条目名 → 是否目录)。依据：同一目标子树的读写全部经由本实例，
    // 且同一目标路径被 FileOperator._targetGates 串行化、写操作同步维护缓存 → 实例内一致。
    // 外部进程并发写同一目录不感知（个人备份场景可接受）；加载失败不入缓存，回退单文件探测。
    // 两层都用 ConcurrentDictionary：ExistsAsync 免锁快路径的读与锁外缓存更新的写并发共存。
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _dirCache = new();

    public SmbFileStorage(string address, string username, string password)
    {
        var (server, share, path) = ParseAddress(address);
        _server = server;
        _share = share;
        _baseSmbPath = path;
        _username = username ?? "";
        _password = password ?? "";
    }

    /// <summary>解析 \\server\share[\path] 或 server/share[/path]；服务器段可以是主机名或 IP。</summary>
    public static (string Server, string Share, string Path) ParseAddress(string address)
    {
        var trimmed = address.Trim().TrimStart('\\', '/');
        var parts = trimmed.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            throw new ArgumentException($"SMB 地址格式无效：{address}（应为 \\\\服务器\\共享名[\\路径]）");
        var path = string.Join('\\', parts[2..]);
        return (parts[0], parts[1], path);
    }

    private string ToSmbPath(string relativePath)
        => string.Join('\\', new[] { _baseSmbPath, relativePath.Replace('/', '\\') }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>按 '/' 拆出父目录与叶子名（计划路径恒为 '/' 分隔）。</summary>
    private static (string Dir, string Leaf) SplitParent(string relativePath)
    {
        var idx = relativePath.LastIndexOf('/');
        return idx < 0 ? ("", relativePath) : (relativePath[..idx], relativePath[(idx + 1)..]);
    }

    /// <summary>
    /// 单次 SMB2 写入字节数：取「协商的 MaxWriteSize」与「1MB 硬上限」的较小值。
    /// 连接建立后才有意义，EnsureConnected 之后调用。
    /// </summary>
    private int WriteChunkSize
    {
        get
        {
            var negotiated = (long)(_client?.MaxWriteSize ?? 0);
            var limit = negotiated > 0
                ? Math.Min(negotiated, MaxWriteChunkSize)
                : FallbackWriteChunkSize;
            return (int)Math.Clamp(limit, 4096, MaxWriteChunkSize);
        }
    }

    private void EnsureConnected()
    {
        if (_store is not null) return;

        var client = new SMB2Client();
        if (!client.Connect(_server, SMBTransportType.DirectTCPTransport))
            throw new IOException($"无法连接 SMB 服务器 {_server}:445（请检查地址与网络；若服务器强制 SMB 加密则不受支持）");
        try
        {
            var (domain, user) = SplitUser(_username);
            var loginStatus = client.Login(domain, user, _password);
            if (loginStatus != NTStatus.STATUS_SUCCESS)
                throw new IOException($"SMB 登录失败（{user}@{_server}）：{loginStatus}。首版不支持匿名/guest 访问，请检查账号密码。");

            var store = client.TreeConnect(_share, out var treeStatus);
            if (treeStatus != NTStatus.STATUS_SUCCESS || store is null)
                throw new IOException($"连接共享 \\{_server}\\{_share} 失败：{treeStatus}");
            _client = client;
            _store = store;
            // 弱网下若协商到 64KB，单流吞吐会掉一个数量级（评估文档 §3.3），此日志是排查首要检查项
            CoreLog.Info($"[SMB] \\\\{_server}\\{_share} 已连接：协商 MaxWriteSize={client.MaxWriteSize}，实际写入块={WriteChunkSize} 字节");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmbFileStorage] Connect/Login failed for {_server}: {ex.Message}");
            client.Disconnect();
            throw;
        }
    }

    private static (string Domain, string User) SplitUser(string username)
    {
        var idx = username.IndexOf('\\');
        return idx > 0 ? (username[..idx], username[(idx + 1)..]) : ("", username);
    }

    private void ResetConnection()
    {
        var store = _store;
        var client = _client;
        _store = null;
        _client = null;
        try { store?.Disconnect(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SmbFileStorage] Store disconnect failed: {ex.Message}"); }
        try { client?.Disconnect(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SmbFileStorage] Client disconnect failed: {ex.Message}"); }
    }

    /// <summary>串行执行一次 SMB 操作；传输层失效（连接被断/会话失效）时置脏连接，让下次调用重建（P2-1）。</summary>
    private async Task<T> InvokeAsync<T>(Func<ISMBFileStore, T> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            EnsureConnected();
            return action(_store!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // P2-1：真实的连接断开（WiFi 漫游、手机休眠、NAS 重启）在 SMBLibrary 里表现为
            // IOException/SocketException/NTStatus 错误码，而非 ObjectDisposedException。
            // 只认 ObjectDisposedException 重连会让 FileOperator 的三次退避重试全部撞在死连接上。
            // 此处对任何操作异常都置脏并重建，确保下一次操作走新连接；具体错误交由上层重试。
            ResetConnection();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IOException SmbError(string operation, NTStatus status, string path)
        => new($"SMB {operation}失败（{path}）：{status}");

    public async Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
    {
        var (dirPath, leaf) = SplitParent(relativePath);
        if (_dirCache.TryGetValue(dirPath, out var listing))
            return listing.TryGetValue(leaf, out var isDir) && !isDir;

        // 目录列表加载失败（目录不存在等 NTStatus 错误）→ 回退单文件探测，保持原语义
        if (await InvokeAsync(store => TryListDir(store, dirPath), ct))
            return _dirCache[dirPath].TryGetValue(leaf, out var isDir2) && !isDir2;
        return await ProbeExistsAsync(relativePath, ct);
    }

    /// <summary>
    /// 拉取目录完整列表入缓存。NTStatus 失败（含目录不存在）返回 false 且不入缓存；
    /// socket 级异常照抛（经 InvokeAsync 置脏重连）。SMBLibrary 的 QueryDirectory 内部已按
    /// MaxTransactSize 循环续传至 NO_MORE_FILES，单次调用即完整列表（SMB2FileStore.cs 已核实）。
    /// </summary>
    private bool TryListDir(ISMBFileStore store, string dirPath)
    {
        if (_dirCache.ContainsKey(dirPath)) return true;

        var openPath = dirPath.Length == 0 ? "\\" : dirPath;
        var status = store.CreateFile(out var handle, out _, openPath, AccessMask.GENERIC_READ,
            FileAttributes.Normal, ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE, null);
        if (status != NTStatus.STATUS_SUCCESS)
            return false;
        try
        {
            var queryStatus = store.QueryDirectory(out var entries, handle, "*", FileInformationClass.FileDirectoryInformation);
            if (queryStatus != NTStatus.STATUS_SUCCESS && queryStatus != NTStatus.STATUS_NO_MORE_FILES)
                return false;
            var listing = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
                if (entry is FileDirectoryInformation info && info.FileName is not "." and not "..")
                    listing[info.FileName] = info.FileAttributes.HasFlag(FileAttributes.Directory);
            _dirCache[dirPath] = listing;
            return true;
        }
        finally
        {
            store.CloseFile(handle);
        }
    }

    /// <summary>写操作后同步维护目录列表缓存（仅当该目录列表已被加载过；present=false 表示条目已消失）。</summary>
    private void UpdateDirCache(string relativePath, bool present, bool isDir = false)
    {
        var (dirPath, leaf) = SplitParent(relativePath);
        if (!_dirCache.TryGetValue(dirPath, out var listing)) return;
        if (present) listing[leaf] = isDir;
        else listing.TryRemove(leaf, out _);
    }

    /// <summary>缓存未命中/加载失败时的原语义路径：CreateFile 单文件探测。</summary>
    private Task<bool> ProbeExistsAsync(string relativePath, CancellationToken ct)
        => InvokeAsync(store =>
        {
            var path = ToSmbPath(relativePath);
            var status = store.CreateFile(out var handle, out _, path, AccessMask.GENERIC_READ,
                FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE, null);
            if (status == NTStatus.STATUS_SUCCESS)
            {
                store.CloseFile(handle);
                return true;
            }
            if (status is NTStatus.STATUS_OBJECT_NAME_NOT_FOUND or NTStatus.STATUS_OBJECT_PATH_NOT_FOUND
                or NTStatus.STATUS_FILE_IS_A_DIRECTORY)
                return false;
            throw SmbError("查询", status, path);
        }, ct);

    public Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default)
        => InvokeAsync<object?>(store =>
        {
            // 逐级创建（幂等）：实例内 _knownDirs 命中即跳过（每文件省 3~4 次往返）
            var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var dir = _baseSmbPath;
            foreach (var segment in segments)
            {
                var parent = dir;
                dir = string.IsNullOrEmpty(dir) ? segment : dir + '\\' + segment;
                if (_knownDirs.ContainsKey(dir)) continue;
                var status = store.CreateFile(out var handle, out _, dir, AccessMask.GENERIC_WRITE,
                    FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_CREATE,
                    CreateOptions.FILE_DIRECTORY_FILE, null);
                if (status == NTStatus.STATUS_SUCCESS)
                    store.CloseFile(handle);
                else if (status is not (NTStatus.STATUS_OBJECT_NAME_COLLISION or NTStatus.STATUS_OBJECT_NAME_EXISTS))
                    throw SmbError("创建目录", status, dir);
                _knownDirs[dir] = true;
                // 已缓存的父列表补上新目录条目，保持列表完整
                if (_dirCache.TryGetValue(parent, out var parentListing))
                    parentListing[segment] = true;
            }
            return null;
        }, ct);

    public Task<long> GetLengthAsync(string relativePath, CancellationToken ct = default)
        => InvokeAsync(store =>
        {
            var path = ToSmbPath(relativePath);
            var status = store.CreateFile(out var handle, out _, path, AccessMask.GENERIC_READ,
                FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE, null);
            if (status != NTStatus.STATUS_SUCCESS)
                return -1L;
            try
            {
                var infoStatus = store.GetFileInformation(out var info, handle, FileInformationClass.FileStandardInformation);
                if (infoStatus != NTStatus.STATUS_SUCCESS || info is not FileStandardInformation standard)
                    return -1L;
                return standard.EndOfFile;
            }
            finally
            {
                store.CloseFile(handle);
            }
        }, ct);

    public async Task<long> CopyFromAsync(IMediaSource source, string relativeTarget, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var path = ToSmbPath(relativeTarget);

        // 打开句柄（持锁，懒建连接）：FILE_OVERWRITE_IF 覆盖上次重试/崩溃残留的 .mo-tmp
        object? handle = null;
        await InvokeAsync<object?>(store =>
        {
            var status = store.CreateFile(out var h, out _, path, AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_OVERWRITE_IF,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
            if (status != NTStatus.STATUS_SUCCESS)
                throw SmbError("写入", status, path);
            handle = h;
            return null;
        }, ct);
        UpdateDirCache(relativeTarget, present: true);

        // 读写流水线：源端预读（不持锁，Task.Run 强制线程池并行，SAF 流的同步 Read 不会阻塞写）
        // 与 SMB 写入（每块独立过 _gate）重叠，隐藏源端读取时间——原实现读写在锁内完全串行。
        // 连接失效时 InvokeAsync 置脏，FileOperator 按整文件退避重试（FILE_OVERWRITE_IF 重开覆盖半成品）。
        var writeChunk = WriteChunkSize;
        var buffers = new[] { new byte[writeChunk], new byte[writeChunk] };
        Stream? src = null;
        Task<int>? prefetch = null;
        long offset = 0, total = 0;
        try
        {
            src = source.OpenRead();
            var currentLen = await src.ReadAsync(buffers[0].AsMemory(), ct);
            var cur = 0;
            while (currentLen > 0)
            {
                ct.ThrowIfCancellationRequested();
                var next = cur ^ 1;
                prefetch = Task.Run(() => src.ReadAsync(buffers[next].AsMemory(), ct).AsTask(), ct);
                var buffer = buffers[cur];
                var len = currentLen;
                int written = 0;
                await InvokeAsync<object?>(store =>
                {
                    // 单次 WriteFile 载荷不得超过协商 MaxWriteSize（SMBLibrary 不自动分片，超限必被拒）
                    var writeStatus = store.WriteFile(out written, handle!, offset, buffer[..len]);
                    if (writeStatus != NTStatus.STATUS_SUCCESS)
                        throw SmbError("写入", writeStatus, $"{path}（偏移 {offset}，长度 {len}，单次写入上限 {writeChunk}）");
                    if (written <= 0)
                        throw new IOException($"SMB 写入未推进（{path}，偏移 {offset}，长度 {len}）：{writeStatus}");
                    return null;
                }, ct);
                offset += written;
                total += written;
                progress?.Report(offset);

                currentLen = await prefetch;
                prefetch = null;
                cur = next;
            }
            return total;
        }
        finally
        {
            // 未消费的预读必须等待收尾，避免对已释放流的未观察异常
            if (prefetch is not null)
            {
                try { await prefetch; }
                catch { /* 主流程已报错，吞掉预读失败 */ }
            }
            try { src?.Dispose(); }
            catch { /* 尽力释放 */ }

            // 关闭句柄（持锁）；连接已被置脏时句柄随连接丢弃，无需 CloseFile
            if (_store is not null && handle is not null)
            {
                try { await InvokeAsync<object?>(s => { s.CloseFile(handle); return null; }, CancellationToken.None); }
                catch { /* 尽力关闭 */ }
            }
        }
    }

    public async Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        await InvokeAsync<object?>(store =>
        {
            var path = ToSmbPath(relativePath);
            var status = store.CreateFile(out var handle, out _, path, AccessMask.DELETE | AccessMask.SYNCHRONIZE,
                FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_DELETE_ON_CLOSE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
            if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND)
                return null;
            if (status != NTStatus.STATUS_SUCCESS)
                throw SmbError("删除", status, path);
            store.CloseFile(handle);
            return null;
        }, ct);
        UpdateDirCache(relativePath, present: false);

        // 删的是目录时，同步失效目录创建缓存与父列表中的条目
        if (_knownDirs.TryRemove(ToSmbPath(relativePath), out _))
        {
            var (parentPath, leaf) = SplitParent(relativePath);
            if (_dirCache.TryGetValue(ToSmbPath(parentPath), out var parentListing))
                parentListing.TryRemove(leaf, out _);
        }
    }

    public async Task MoveAsync(string relativeFrom, string relativeTo, CancellationToken ct = default)
    {
        await InvokeAsync<object?>(store =>
        {
            var from = ToSmbPath(relativeFrom);
            var to = ToSmbPath(relativeTo);

            // 注：不再前置 DeleteIfExists——.mo-tmp 临时名唯一（_targetGates 保证同路径互斥），
            // SetFileInformation(ReplaceIfExists=true) 自身即可覆盖上次中断/重试的残留（每文件省 2 次往返）
            var status = store.CreateFile(out var handle, out _, from,
                AccessMask.DELETE | AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
                FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
            if (status != NTStatus.STATUS_SUCCESS)
                throw SmbError("重命名(打开)", status, from);
            try
            {
                // 同一 share 内 rename（.mo-tmp → 最终名），info level 10
                var renameStatus = store.SetFileInformation(handle,
                    new FileRenameInformationType2 { ReplaceIfExists = true, FileName = to });
                if (renameStatus != NTStatus.STATUS_SUCCESS)
                {
                    // 部分老设备（SMB 2.0.2）不支持 info level 10，退回 level 3 的 FileRenameInformation
                    var legacyStatus = store.SetFileInformation(handle,
                        new FileRenameInformationType1 { ReplaceIfExists = true, FileName = to });
                    if (legacyStatus != NTStatus.STATUS_SUCCESS)
                        throw SmbError("重命名", renameStatus, $"{from} → {to}（旧式重命名：{legacyStatus}）");
                }
            }
            finally
            {
                store.CloseFile(handle);
            }
            return null;
        }, ct);

        // rename 成功后同步维护两个目录的列表缓存
        UpdateDirCache(relativeFrom, present: false);
        UpdateDirCache(relativeTo, present: true);
    }

    public Task SetModifiedUtcAsync(string relativePath, DateTime utc, CancellationToken ct = default)
        => InvokeAsync<object?>(store =>
        {
            var path = ToSmbPath(relativePath);
            var status = store.CreateFile(out var handle, out _, path, AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
            if (status != NTStatus.STATUS_SUCCESS)
                throw SmbError("设置时间(打开)", status, path);
            try
            {
                // 未指定的其他时间戳用 mustNotChange 语义保持原值（SMB SetFileInformation）
                var info = new FileBasicInformation
                {
                    CreationTime = new SetFileTime(mustNotChange: true),
                    LastAccessTime = new SetFileTime(mustNotChange: true),
                    ChangeTime = new SetFileTime(mustNotChange: true),
                    LastWriteTime = new SetFileTime(utc)
                };
                var infoStatus = store.SetFileInformation(handle, info);
                if (infoStatus != NTStatus.STATUS_SUCCESS)
                    throw SmbError("设置修改时间", infoStatus, path);
            }
            finally
            {
                store.CloseFile(handle);
            }
            return null;
        }, ct);

    /// <summary>断开 SMB 连接，释放客户端与文件存储句柄。</summary>
    public void Dispose()
    {
        ResetConnection();
        _knownDirs.Clear();
        _dirCache.Clear();
        _gate.Dispose();
    }
}
