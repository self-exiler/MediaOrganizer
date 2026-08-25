namespace MediaOrganizer.Core.Sources;

/// <summary>本地文件系统源（桌面版 / Android 应用专有目录场景）。</summary>
public sealed class LocalMediaSource(string path) : IMediaSource
{
    private readonly FileInfo _info = new(string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("路径不能为空", nameof(path)) : path);

    public string Identifier { get; } = path;
    public string DisplayName => _info.Name;
    public long Length => _info.Length;

    public DateTimeOffset? ModifiedTime
    {
        get
        {
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
