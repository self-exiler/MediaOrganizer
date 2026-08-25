using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Core.Extraction;

/// <summary>
/// 文件系统提取器：以文件修改时间（mtime）兜底。默认禁用。
/// 读取扫描时预取的 IMediaSource.ModifiedTime——SAF 下 File.GetLastWriteTimeUtc(content://…) 会抛异常被吞（ADR-0006 背景点 4）。
/// </summary>
public sealed class FileSystemExtractor(bool enabled, double weight) : IDateExtractor
{
    public string Name => "FileSystem";
    public bool Enabled { get; } = enabled;
    public double Weight { get; } = weight >= 0
        ? weight
        : throw new ArgumentOutOfRangeException(nameof(weight), "must be non-negative");

    public DateTimeOffset? Extract(MediaFile file)
        => file.Source?.ModifiedTime;
}
