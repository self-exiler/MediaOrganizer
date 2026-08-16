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
/// 8MB 分块流式、.mo-tmp 临时名 + 大小校验、重试退避由 FileOperator 层统一保证。
/// </summary>
public sealed class SmbFileStorage : IFileStorage
{
    private const int ChunkSize = 8 * 1024 * 1024;

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
        catch
        {
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
        try { store?.Disconnect(); } catch { /* 连接已断 */ }
        try { client?.Disconnect(); } catch { /* 连接已断 */ }
    }

    /// <summary>串行执行一次 SMB 操作；传输层失效（连接被断/会话失效）时重建连接重试一次。</summary>
    private async Task<T> InvokeAsync<T>(Func<ISMBFileStore, T> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            EnsureConnected();
            try
            {
                return action(_store!);
            }
            catch (Exception ex) when (ex is ObjectDisposedException)
            {
                ResetConnection();
                EnsureConnected();
                return action(_store!);
            }
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
                var buffer = new byte[ChunkSize];
                using var src = source.OpenRead();
                long offset = 0;
                int read;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var writeStatus = store.WriteFile(out var written, handle, offset, buffer[..read]);
                    if (writeStatus != NTStatus.STATUS_SUCCESS)
                        throw SmbError("写入", writeStatus, path);
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
            var status = store.CreateFile(out var handle, out _, from,
                AccessMask.DELETE | AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
                FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
            if (status != NTStatus.STATUS_SUCCESS)
                throw SmbError("重命名(打开)", status, from);
            try
            {
                // 同一 share 内 rename（.mo-tmp → 最终名）
                var rename = new FileRenameInformationType2 { FileName = to };
                var renameStatus = store.SetFileInformation(handle, rename);
                if (renameStatus != NTStatus.STATUS_SUCCESS)
                    throw SmbError("重命名", renameStatus, $"{from} → {to}");
            }
            finally
            {
                store.CloseFile(handle);
            }
            return null;
        }, ct);

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
}
