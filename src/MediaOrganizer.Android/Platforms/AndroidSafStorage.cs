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
    private const int BufferSize = 1024 * 1024; // 池化读写缓冲 1MB（P2-10：原 8MB 一次性 new 进 LOH）

    private readonly ContentResolver _resolver;
    private readonly DocumentFile _root;
    // perf-19：按目录相对路径缓存已解析 DocumentFile。同批归档文件共享目录，
    // 原实现每文件逐段 FindFile = 每段一次 SAF provider IPC（内部枚举子项）。
    // 实例生命周期 = 一次执行，会话内目录结构只增不减，缓存恒新鲜。
    private readonly Dictionary<string, DocumentFile> _dirCache = new(StringComparer.Ordinal);

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

    /// <summary>解析目录部分（"" = 根）；createDirs=true 时逐级创建并写入缓存，失败（不存在）不缓存。</summary>
    private DocumentFile? ResolveDir(string dirRelative, bool createDirs)
    {
        if (dirRelative.Length == 0) return _root;
        if (_dirCache.TryGetValue(dirRelative, out var cached)) return cached;

        var current = _root;
        foreach (var seg in dirRelative.Split('/'))
        {
            var existing = current.FindFile(seg);
            if (existing is not null)
            {
                current = existing;
                continue;
            }
            if (!createDirs) return null;
            current = current.CreateDirectory(seg)
                      ?? throw new IOException($"SAF 创建目录失败：{dirRelative}");
        }
        _dirCache[dirRelative] = current;
        return current;
    }

    /// <summary>把相对路径解析为树内 DocumentFile；createDirs=true 时父目录与文件本身不存在则创建。</summary>
    private DocumentFile? Resolve(string relativePath, bool createDirs)
    {
        var relative = relativePath.Replace('\\', '/');
        var idx = relative.LastIndexOf('/');
        var dirPart = idx < 0 ? "" : relative[..idx];
        var leaf = relative[(idx + 1)..];

        var dir = ResolveDir(dirPart, createDirs);
        if (dir is null) return null;

        var doc = dir.FindFile(leaf);
        if (doc is null && createDirs)
            doc = dir.CreateFile("application/octet-stream", leaf)
                ?? throw new IOException($"SAF 创建失败：{relativePath}");
        return doc;
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

    public async Task<long> CopyFromAsync(IMediaSource source, string relativeTarget, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var target = Resolve(relativeTarget, createDirs: true)
                     ?? throw new IOException($"无法创建目标：{relativeTarget}");
        return await Task.Run(async () =>
        {
            await using var src = source.OpenRead();
            await using var dst = _resolver.OpenOutputStream(target.Uri)
                                  ?? throw new IOException($"无法打开输出流：{relativeTarget}");
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                long copied = 0;
                int read;
                while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    copied += read;
                    progress?.Report(copied);
                }
                return copied;
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
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
        var newName = relativeTo[(relativeTo.LastIndexOf('/') + 1)..];
        return Task.Run(() =>
        {
            // 同目录重命名优先 DocumentsContract.RenameDocument：元数据层操作，不重写文件数据。
            // FileOperator 的 .mo-tmp 与最终名恒在同目录，满足其前提（评审 2.9：消除流复制导致的整文件双写）
            try
            {
                var renamed = global::Android.Provider.DocumentsContract.RenameDocument(_resolver, from.Uri, newName);
                if (renamed is not null) return;
            }
            catch (Exception ex)
            {
                // 提供方不支持/拒绝 rename（如跨存储卷）：回退流复制 + 删源，行为等价
                System.Diagnostics.Debug.WriteLine($"[AndroidSafStorage] RenameDocument 不可用，回退流复制：{ex.Message}");
            }

            var to = Resolve(relativeTo, createDirs: true)
                     ?? throw new IOException($"无法创建目标：{relativeTo}");
            using var src = _resolver.OpenInputStream(from.Uri)
                            ?? throw new IOException("无法读取临时文件");
            using var dst = _resolver.OpenOutputStream(to.Uri)
                            ?? throw new IOException("无法写入目标文件");
            src.CopyTo(dst);
            from.Delete();
        }, ct);
    }

    public Task SetModifiedUtcAsync(string relativePath, DateTime utc, CancellationToken ct = default)
    {
        // SAF 不支持设置修改时间（DocumentsContract 无此 API）→ 静默跳过（FR-A5.5）
        return Task.CompletedTask;
    }

    /// <summary>SAF 存储无可释放资源（实现 IFileStorage 的 IDisposable 契约）。</summary>
    public void Dispose()
    {
    }
}
