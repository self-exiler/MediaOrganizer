using System.Collections.Concurrent;
using System.Net;
using MediaOrganizer.Core.Sources;
using WebDAVClient;
using WebDAVClient.Helpers;

namespace MediaOrganizer.Core.Storage;

/// <summary>WebDAV 存储（FR-10，全平台）。基于 WebDAVClient 2.7.0（接口无 Async 后缀）。</summary>
public sealed class WebDavFileStorage : IFileStorage
{
    private readonly IClient _client;
    private readonly string _basePath;
    // FileOperator 对网络目标以并行度 4 并发调用 CreateDirectoryAsync，
    // 此处必须是并发安全集合 —— HashSet 并发 Add 会损坏内部桶结构。
    private readonly ConcurrentDictionary<string, bool> _knownDirs = new();

    public WebDavFileStorage(string baseAddress, string username, string password)
    {
        _client = new Client(new NetworkCredential(username, password));
        // 防御性清洗：旧配置可能含不可见/全角污染字符（手机输入法），清洗后再解析（含合法性校验）
        var uri = new Uri(WebDavAddress.Sanitize(baseAddress));
        // 库内部直接 new UriBuilder(Server)，Server 必须是含 scheme 的完整 URI，
        // 赋 uri.Host 会让 UriBuilder 抛 UriFormatException(BadUriFormat)
        _client.Server = uri.GetLeftPart(UriPartial.Authority);
        _client.BasePath = uri.AbsolutePath.TrimEnd('/');
        _basePath = _client.BasePath;
    }

    /// <summary>测试/DI 注入口：直接使用已配置好的 IClient（basePath 含前导 '/' 或为空串）。</summary>
    public WebDavFileStorage(IClient client, string basePath)
    {
        _client = client;
        _basePath = basePath;
    }

    private string Resolve(string relativePath)
        => _basePath + "/" + relativePath.TrimStart('/'); // _basePath 构造时已 TrimEnd（perf-9）

    private static string ParentOf(string relativePath)
    {
        var idx = relativePath.LastIndexOf('/');
        return idx <= 0 ? "" : relativePath[..idx];
    }

    public async Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
    {
        try
        {
            var item = await _client.GetFile(Resolve(relativePath), ct);
            return item is not null;
        }
        catch (WebDAVException ex) when (ex.GetHttpCode() == 404)
        {
            // 仅 404 = 确定不存在；错误码 0 / HTTP 异常 / 其他状态码一律上抛，
            // 由 FileOperator 的退避重试接管——瞬时故障误判「不存在」会让 Skip 策略形同虚设（审计 4）
            return false;
        }
    }

    /// <summary>建目录专用的宽容版：查询失败即视为不存在并尝试创建，误判由 WebDAVConflictException 兜底。</summary>
    private async Task<bool> ExistsForCreateAsync(string relativePath, CancellationToken ct)
    {
        try
        {
            return await ExistsAsync(relativePath, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDavFileStorage] 目录探测失败按不存在处理 {relativePath}：{ex.Message}");
            return false;
        }
    }

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default)
    {
        // 逐级创建，缺哪层补哪层（幂等），已知目录跳过 HTTP 查询
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var seg in segments)
        {
            current = current.Length == 0 ? seg : current + "/" + seg;
            if (_knownDirs.ContainsKey(current)) continue;
            if (!await ExistsForCreateAsync(current + "/", ct))
            {
                try
                {
                    // 传入完整父路径（含 basePath），避免库内部拼接产生双斜杠
                    await _client.CreateDir(Resolve(ParentOf(current)), seg, ct);
                }
                catch (WebDAVConflictException)
                {
                    // 并发/已存在
                }
            }
            _knownDirs[current] = true;
        }
    }

    public async Task<long> GetLengthAsync(string relativePath, CancellationToken ct = default)
    {
        try
        {
            var item = await _client.GetFile(Resolve(relativePath), ct);
            return item?.ContentLength ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    public async Task<long> CopyFromAsync(IMediaSource source, string relativeTarget, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        // perf-9：Upload 本身是异步库调用，直接 await，勿再包 Task.Run 多一次线程切换
        var parent = ParentOf(relativeTarget);
        var name = relativeTarget[(relativeTarget.LastIndexOf('/') + 1)..];
        await using var src = source.OpenRead();
        var ok = await _client.Upload(Resolve(parent), src, name, null, ct);
        if (!ok) throw new IOException($"WebDAV 上传失败：{relativeTarget}");
        progress?.Report(source.Length);
        // 上传后回查服务端实际长度自证（审计 4）：直接返回源长度会让 FileOperator 的大小校验恒真
        var actual = await GetLengthAsync(relativeTarget, ct);
        if (actual < 0)
            throw new IOException($"WebDAV 上传后无法回查文件大小：{relativeTarget}");
        if (actual != source.Length)
            throw new IOException($"WebDAV 上传大小校验失败：期望 {source.Length}，实际 {actual}");
        return actual;
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        => _client.DeleteFile(Resolve(relativePath), null, ct);

    public Task MoveAsync(string relativeFrom, string relativeTo, CancellationToken ct = default)
        => _client.MoveFile(Resolve(relativeFrom), Resolve(relativeTo), overwrite: true, null, null, ct);

    public Task SetModifiedUtcAsync(string relativePath, DateTime utc, CancellationToken ct = default)
    {
        // WebDAV 的 DAV:getlastmodified 属保留命名空间，客户端库无法写入 → 静默跳过（ADR FR-10.8）
        return Task.CompletedTask;
    }

    /// <summary>释放底层 HTTP 客户端（若库实现 IDisposable）。</summary>
    public void Dispose()
        => (_client as IDisposable)?.Dispose();
}
