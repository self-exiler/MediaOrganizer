using Android.Content;
using AndroidX.DocumentFile.Provider;
using MediaOrganizer.Core.Sources;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// Android SAF 输出目标存储（FR-A5.8）：treeUri 为输出目录树 URI。
/// 相对路径（'/' 分隔）逐级解析到树内，DocumentFile 创建目录 + ContentResolver 流式写入；
/// .mo-tmp 临时名 + 大小校验 + 改名语义由 Core FileOperator 统一保证。
/// </summary>
public sealed class AndroidSafStorage : IFileStorage
{
    private const int ChunkSize = 8 * 1024 * 1024; // 8MB 分块（FR-A9.5 同语义）

    private readonly ContentResolver _resolver;
    private readonly DocumentFile _root;

    public AndroidSafStorage(string treeUri, ContentResolver? resolver = null)
    {
        _resolver = resolver ?? global::Android.App.Application.Context.ContentResolver!;
        var context = global::Android.App.Application.Context;
        var uri = global::Android.Net.Uri.Parse(treeUri)
                  ?? throw new ArgumentException($"无效的 SAF 树 URI：{treeUri}");
        _root = DocumentFile.FromTreeUri(context, uri)
                ?? throw new IOException($"无法打开 SAF 目录：{treeUri}");
        if (!_root.CanWrite())
            throw new IOException($"输出目录不可写：{treeUri}");
    }

    /// <summary>把相对路径逐级解析为树内 DocumentFile；createDirs=true 时父目录不存在则逐级创建。</summary>
    private DocumentFile? Resolve(string relativePath, bool createDirs)
    {
        var current = _root;
        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            var existing = current.FindFile(segments[i]);
            if (existing is not null)
            {
                current = existing;
                continue;
            }
            if (!createDirs) return null;
            var isLast = i == segments.Length - 1;
            current = isLast
                ? current.CreateFile("application/octet-stream", segments[i])
                : current.CreateDirectory(segments[i]);
            if (current is null)
                throw new IOException($"SAF 创建失败：{string.Join("/", segments[..(i + 1)])}");
        }
        return current;
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
        => Task.FromResult(Resolve(relativePath, createDirs: false) is not null);

    public Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default)
    {
        Resolve(relativePath, createDirs: true);
        return Task.CompletedTask;
    }

    public Task<long> GetLengthAsync(string relativePath, CancellationToken ct = default)
        => Task.FromResult(Resolve(relativePath, createDirs: false)?.Length() ?? -1L);

    public Task CopyFromAsync(IMediaSource source, string relativeTarget, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var target = Resolve(relativeTarget, createDirs: true)
                     ?? throw new IOException($"无法创建目标：{relativeTarget}");
        return Task.Run(async () =>
        {
            await using var src = source.OpenRead();
            await using var dst = _resolver.OpenOutputStream(target.Uri)
                                  ?? throw new IOException($"无法打开输出流：{relativeTarget}");
            var buffer = new byte[ChunkSize];
            long copied = 0;
            int read;
            while ((read = await src.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                copied += read;
                progress?.Report(copied);
            }
        }, ct);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        Resolve(relativePath, createDirs: false)?.Delete();
        return Task.CompletedTask;
    }

    public Task MoveAsync(string relativeFrom, string relativeTo, CancellationToken ct = default)
    {
        var from = Resolve(relativeFrom, createDirs: false)
                   ?? throw new IOException($"临时文件不存在：{relativeFrom}");
        var to = Resolve(relativeTo, createDirs: true)
                 ?? throw new IOException($"无法创建目标：{relativeTo}");
        return Task.Run(() =>
        {
            // SAF 无跨文档 rename：流复制 + 删除源（.mo-tmp → 最终名的等价语义）
            using var src = _resolver.OpenInputStream(from.Uri)
                            ?? throw new IOException("无法读取临时文件");
            using var dst = _resolver.OpenOutputStream(to.Uri)
                            ?? throw new IOException("无法写入目标文件");
            src.CopyTo(dst);
            from.Delete();
        });
    }

    public Task SetModifiedUtcAsync(string relativePath, DateTime utc, CancellationToken ct = default)
    {
        // SAF 不支持设置修改时间（DocumentsContract 无此 API）→ 静默跳过（FR-A5.5）
        return Task.CompletedTask;
    }
}
