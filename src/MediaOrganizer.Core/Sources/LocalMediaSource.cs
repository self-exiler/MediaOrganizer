namespace MediaOrganizer.Core.Sources;

/// <summary>本地文件系统源（桌面版 / Android 应用专有目录场景）。</summary>
public sealed class LocalMediaSource : IMediaSource
{
    private readonly FileInfo _info;
    // perf-4：扫描时预取的长度/mtime（FileScanner 枚举时 FileInfo 已持有，避免提取链每文件重复 stat）；null = 按需读取
    private readonly long? _length;
    private readonly DateTimeOffset? _modifiedAt;

    public LocalMediaSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("路径不能为空", nameof(path));
        _info = new FileInfo(path);
        Identifier = path;
    }

    /// <summary>扫描预取构造：FileScanner 枚举时已拿到 LastWriteTimeUtc/Length，直接注入。</summary>
    public LocalMediaSource(string path, DateTime lastWriteTimeUtc, long? length = null) : this(path)
    {
        _length = length;
        _modifiedAt = lastWriteTimeUtc == DateTime.MinValue
            ? null
            : new DateTimeOffset(lastWriteTimeUtc, TimeSpan.Zero);
    }

    public string Identifier { get; }

    public string DisplayName => _info.Name;

    public long Length => _length ?? _info.Length;

    public DateTimeOffset? ModifiedTime
    {
        get
        {
            if (_modifiedAt is { } cached) return cached;
            try
            {
                var utc = File.GetLastWriteTimeUtc(Identifier);
                return utc == DateTime.MinValue ? null : new DateTimeOffset(utc, TimeSpan.Zero);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocalMediaSource] ModifiedTime read failed for {Identifier}: {ex.Message}");
                return null;
            }
        }
    }

    public Stream OpenRead()
        => new FileStream(Identifier, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);

    public void Delete() => File.Delete(Identifier);
}
