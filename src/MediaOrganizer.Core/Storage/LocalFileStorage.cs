using System.Buffers;
using MediaOrganizer.Core.Sources;

namespace MediaOrganizer.Core.Storage;

/// <summary>本地文件系统存储。</summary>
public class LocalFileStorage(string rootPath) : IFileStorage
{
    protected readonly string Root = string.IsNullOrWhiteSpace(rootPath)
        ? throw new ArgumentException("rootPath cannot be null or empty.", nameof(rootPath))
        : Path.GetFullPath(rootPath);

    // Windows 文件系统大小写不敏感；Linux/Android 大小写敏感但不会因大小写产生路径逃逸，统一用 OrdinalIgnoreCase 更宽松安全。
    private static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

    protected string Resolve(string relativePath)
    {
        var rel = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(Root, rel));
        // 防路径穿越：相对路径含 ".." 段时不允许逃出根目录（文件名来自扫描结果，属不可信输入）
        var rootPrefix = Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, PathComparison) && !full.Equals(Root, PathComparison))
            throw new IOException($"目标路径越出输出根目录：{relativePath}");
        return full;
    }

    public virtual Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
        => Task.FromResult(File.Exists(Resolve(relativePath)));

    public virtual Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Resolve(relativePath));
        return Task.CompletedTask;
    }

    public virtual Task<long> GetLengthAsync(string relativePath, CancellationToken ct = default)
    {
        var info = new FileInfo(Resolve(relativePath));
        // P3-7：与契约一致——文件不可用返回 -1，而非抛 FileNotFoundException
        return Task.FromResult(info.Exists ? info.Length : -1L);
    }

    public virtual async Task<long> CopyFromAsync(IMediaSource source, string relativeTarget, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var target = Resolve(relativeTarget);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var src = source.OpenRead();
        await using var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        var buffer = ArrayPool<byte>.Shared.Rent(8 * 1024 * 1024);
        try
        {
            long copied = 0;
            int read;
            while ((read = await src.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                copied += read;
                progress?.Report(copied);
            }
            return copied;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public virtual Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var path = Resolve(relativePath);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public virtual Task MoveAsync(string relativeFrom, string relativeTo, CancellationToken ct = default)
    {
        var from = Resolve(relativeFrom);
        var to = Resolve(relativeTo);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (File.Exists(to)) File.Delete(to);
        File.Move(from, to);
        return Task.CompletedTask;
    }

    public virtual Task SetModifiedUtcAsync(string relativePath, DateTime utc, CancellationToken ct = default)
    {
        File.SetLastWriteTimeUtc(Resolve(relativePath), utc);
        return Task.CompletedTask;
    }

    /// <summary>本地存储无可释放资源（实现 IFileStorage 的 IDisposable 契约）。</summary>
    public virtual void Dispose()
    {
    }
}
