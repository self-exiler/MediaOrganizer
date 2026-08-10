using System.Net;
using WebDAVClient;
using WebDAVClient.Helpers;

namespace MediaOrganizer.Core.Storage;

/// <summary>WebDAV 存储（FR-10，全平台）。基于 WebDAVClient 2.7.0（接口无 Async 后缀）。</summary>
public sealed class WebDavFileStorage : IFileStorage
{
    private readonly IClient _client;
    private readonly string _basePath;
    private readonly HashSet<string> _knownDirs = new();

    public WebDavFileStorage(string baseAddress, string username, string password)
    {
        _client = new Client(new NetworkCredential(username, password));
        var uri = new Uri(baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/");
        _client.Server = uri.Host;
        _client.BasePath = uri.AbsolutePath.TrimEnd('/');
        if (!uri.IsDefaultPort) _client.Port = uri.Port;
        _basePath = _client.BasePath;
    }

    private string Resolve(string relativePath)
        => _basePath + "/" + relativePath.TrimStart('/');

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
        catch (WebDAVException)
        {
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
            if (_knownDirs.Contains(current)) continue;
            if (!await ExistsAsync(current + "/", ct))
            {
                try
                {
                    await _client.CreateDir(ParentOf(current), seg, ct);
                }
                catch (WebDAVConflictException)
                {
                    // 并发/已存在
                }
            }
            _knownDirs.Add(current);
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

    public Task CopyFromAsync(string localSourcePath, string relativeTarget, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var parent = ParentOf(relativeTarget);
        var name = relativeTarget[(relativeTarget.LastIndexOf('/') + 1)..];
        return Task.Run(async () =>
        {
            await using var src = new FileStream(localSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
            var ok = await _client.Upload(Resolve(parent), src, name, null, ct);
            if (!ok) throw new IOException($"WebDAV 上传失败：{relativeTarget}");
            progress?.Report(src.Length);
        }, ct);
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
}
