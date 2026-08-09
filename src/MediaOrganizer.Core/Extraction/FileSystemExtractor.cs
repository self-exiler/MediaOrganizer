using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Core.Extraction;

/// <summary>文件系统提取器：以文件修改时间（mtime）兜底。默认禁用。</summary>
public sealed class FileSystemExtractor(bool enabled, double weight) : IDateExtractor
{
    public string Name => "FileSystem";
    public bool Enabled { get; } = enabled;
    public double Weight { get; } = weight;

    public DateTimeOffset? Extract(MediaFile file)
    {
        try
        {
            var utc = File.GetLastWriteTimeUtc(file.Path);
            if (utc == DateTime.MinValue) return null;
            return new DateTimeOffset(utc, TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }
}
