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
        _client.Server = uri.Host;
        _client.BasePath = uri.AbsolutePath.TrimEnd('/');
        if (!uri.IsDefaultPort) _client.Port = uri.Port;
        _basePath = _client.BasePath;
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
        catch (WebDAVException ex) when (ex.GetHttpCode() is 404 or 0)
        {
            // 404 = 不存在；0 = 非 HTTP 错误（解析失败等）
            return false;
        }
        catch (HttpRequestException)
        {
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
            if (!await ExistsAsync(current + "/", ct))
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
        // 库按整个流上传且成功即完整，写入自证大小 = 源长度
        return source.Length;
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
