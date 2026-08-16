using Android.Content;
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using MediaOrganizer.Core.Sources;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// Android SAF 输出目标存储（FR-A5.8，ADR-0005 §3）：treeUri 为输出目录树 URI。
/// 目标相对路径（'/' 分隔）解析到树内，通过 DocumentFile 创建目录 + ContentResolver 流式写入。
/// .mo-tmp 临时名 + 大小校验 + 改名（MoveAsync）语义由 FileOperator 统一保证。
/// </summary>
public sealed class AndroidSafStorage : IFileStorage
{
    private const int ChunkSize = 8 * 1024 * 1024;

    private readonly ContentResolver _resolver;
    private readonly DocumentFile _root;
    private readonly string _rootName;

    public AndroidSafStorage(string treeUri, ContentResolver? resolver = null)
    {
        _resolver = resolver ?? global::Android.App.Application.Context.ContentResolver;
        var uri = global::Android.Net.Uri.Parse(treeUri);
        if (uri is null)
            throw new ArgumentException($"无效的 SAF 树 URI：{treeUri}");
        _root = DocumentFile.FromTreeUri(global::Android.App.Application.Context, uri);
        if (_root is null || !_root.CanWrite())
            throw new IOException($"输出目录不可写：{treeUri}");
        _rootName = _root.Name ?? "";
    }

    /// <summary>把相对路径（'/' 分隔）解析为树内 DocumentFile（逐级）。父目录不存在则创建。</summary>
    private DocumentFile Resolve(string relativePath, bool createDirs = true)
    {
        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = _root;
        for (var i = 0; i < segments.Length; i++)
        {
            var existing = current.FindFile(segments[i]);
            if (existing is not null)
            {
                current = existing;
                continue;
            }
            var isLast = i == segments.Length - 1;
            if (createDirs)
                current = isLast ? current.CreateFile("application/octet-stream", segments[i])
                                 : current.CreateDirectory(segments[i]);
            else
                return null!;
        }
        return current;
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
    {
        var resolved = Resolve(relativePath, createDirs: false);
        return Task.FromResult(resolved is not null);
    }

    public Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default)
    {
        Resolve(relativePath, createDirs: true);
        return Task.CompletedTask;
    }

    public Task<long> GetLengthAsync(string relativePath, CancellationToken ct = default)
    {
        var resolved = Resolve(relativePath, createDirs: false);
        return Task.FromResult(resolved?.Length() ?? -1);
    }

    public Task CopyFromAsync(IMediaSource source, string relativeTarget, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var target = Resolve(relativeTarget, createDirs: true);
        return Task.Run(async () =>
        {
            await using var src = source.OpenRead();
            await using var dst = _resolver.OpenOutputStream(target.Uri!);
            var buffer = new byte[ChunkSize];
            long copied = 0;
            int read;
            while ((read = await src.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                copied += read;
                progress?.Report(copied);
            }
        }, ct);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var resolved = Resolve(relativePath, createDirs: false);
        resolved?.Delete();
        return Task.CompletedTask;
    }

    public Task MoveAsync(string relativeFrom, string relativeTo, CancellationToken ct = default)
    {
        // SAF 无原子 rename：复制 + 删除（.mo-tmp → 最终名语义在 SAF 上等价）
        var from = Resolve(relativeFrom, createDirs: false);
        var to = Resolve(relativeTo, createDirs: true);
        if (from is null || to is null) return Task.CompletedTask;
        if (from.Uri != to.Uri)
        {
            var tmp = _resolver.OpenInputStream(from.Uri!);
            using var dst = _resolver.OpenOutputStream(to.Uri!);
            tmp.CopyTo(dst);
            tmp.Close();
            from.Delete();
        }
        return Task.CompletedTask;
    }

    public Task SetModifiedUtcAsync(string relativePath, DateTime utc, CancellationToken ct = default)
    {
        // SAF 不支持设置文件修改时间（DocumentsContract 无此 API）→ 静默跳过（FR-A5.5）
        return Task.CompletedTask;
    }
}