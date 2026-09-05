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
/// SMBLibrary 客户端非线程安全，所有操作经 _gate 串行化（FileOperator 并行度 > 1 时退化为顺序写）。
/// 写入按 min(协商 MaxWriteSize, 1MB) 分块（SMBLibrary 的 WriteFile 不自动分片，超限必失败）；
/// .mo-tmp 临时名 + 大小校验、重试退避由 FileOperator 层统一保证。
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

    public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
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
            // 逐级创建（幂等）：已存在（OBJECT_NAME_COLLISION / OBJECT_NAME_EXISTS）视为成功
            var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = _baseSmbPath;
            foreach (var segment in segments)
            {
                current = string.IsNullOrEmpty(current) ? segment : current + '\\' + segment;
                var status = store.CreateFile(out var handle, out _, current, AccessMask.GENERIC_WRITE,
                    FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_CREATE,
                    CreateOptions.FILE_DIRECTORY_FILE, null);
                if (status == NTStatus.STATUS_SUCCESS)
                    store.CloseFile(handle);
                else if (status is not (NTStatus.STATUS_OBJECT_NAME_COLLISION or NTStatus.STATUS_OBJECT_NAME_EXISTS))
                    throw SmbError("创建目录", status, current);
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

    public Task CopyFromAsync(IMediaSource source, string relativeTarget, IProgress<long>? progress = null, CancellationToken ct = default)
        => InvokeAsync<object?>(store =>
        {
            var path = ToSmbPath(relativeTarget);
            var status = store.CreateFile(out var handle, out _, path, AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_OVERWRITE_IF,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
            if (status != NTStatus.STATUS_SUCCESS)
                throw SmbError("写入", status, path);
            try
            {
                // 关键：每次 WriteFile 的载荷不得超过协商的 MaxWriteSize，
                // SMBLibrary 不会自动分片，超限请求会被服务器拒绝。
                var writeChunk = WriteChunkSize;
                var buffer = new byte[writeChunk];
                using var src = source.OpenRead();
                long offset = 0;
                int read;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var writeStatus = store.WriteFile(out var written, handle, offset, buffer[..read]);
                    if (writeStatus != NTStatus.STATUS_SUCCESS)
                        throw SmbError("写入", writeStatus,
                            $"{path}（偏移 {offset}，长度 {read}，单次写入上限 {writeChunk}）");
                    if (written <= 0)
                        throw new IOException($"SMB 写入未推进（{path}，偏移 {offset}，长度 {read}）：{writeStatus}");
                    offset += written;
                    progress?.Report(offset);
                }
            }
            finally
            {
                store.CloseFile(handle);
            }
            return null;
        }, ct);

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        => InvokeAsync<object?>(store =>
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

    public Task MoveAsync(string relativeFrom, string relativeTo, CancellationToken ct = default)
        => InvokeAsync<object?>(store =>
        {
            var from = ToSmbPath(relativeFrom);
            var to = ToSmbPath(relativeTo);

            // rename 不带 ReplaceIfExists：目标若有残留（上次中断/重试）会撞 OBJECT_NAME_COLLISION，先清掉
            DeleteIfExists(store, to);

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

    /// <summary>存在则删除（FILE_DELETE_ON_CLOSE 语义）；不存在或删除失败均静默返回。</summary>
    private static void DeleteIfExists(ISMBFileStore store, string path)
    {
        var status = store.CreateFile(out var handle, out _, path, AccessMask.DELETE | AccessMask.SYNCHRONIZE,
            FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DELETE_ON_CLOSE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        if (status == NTStatus.STATUS_SUCCESS)
            store.CloseFile(handle);
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
        _gate.Dispose();
    }
}
