using System.Buffers;

namespace MediaOrganizer.Core.Storage;

/// <summary>本地文件系统存储（也覆盖 SMB UNC 路径，Windows）。</summary>
public class LocalFileStorage(string rootPath) : IFileStorage
{
    protected readonly string Root = Path.GetFullPath(rootPath);

    protected string Resolve(string relativePath)
    {
        var rel = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(Root, rel));
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
        => Task.FromResult(new FileInfo(Resolve(relativePath)).Length);

    public virtual async Task CopyFromAsync(string localSourcePath, string relativeTarget, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var target = Resolve(relativeTarget);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var src = new FileStream(localSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
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
}
