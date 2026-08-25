using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Platforms;

namespace MediaOrganizer.Core.Extraction;

/// <summary>
/// EXIF 提取器（FR-A2.1/FR-A2.3）：图片走注入的 IExifReader 读流（桌面 Magick.NET / Android ExifInterface），
/// 视频走 TagLib# 经 StreamFileAbstraction 从 IMediaSource 流读容器元数据——提取器本体双端共享（ADR-0006 决策 2）。
/// </summary>
public sealed class ExifExtractor(bool enabled, double weight, IExifReader? exifReader) : IDateExtractor
{
    public string Name => "Exif";
    public bool Enabled { get; } = enabled;
    public double Weight { get; } = weight;

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp", ".3g2"
    };

    public DateTimeOffset? Extract(MediaFile file)
    {
        var ext = Path.GetExtension(file.FileName);
        return VideoExtensions.Contains(ext)
            ? ExtractFromVideo(file)
            : ExtractFromImage(file);
    }

    private DateTimeOffset? ExtractFromImage(MediaFile file)
    {
        if (exifReader is null || file.Source is null) return null;
        try
        {
            using var stream = file.Source.OpenRead();
            return exifReader.ReadImageExif(stream);
        }
        catch (Exception ex)
        {
            // 损坏文件/无权限等：一律视为该提取器无结果
            System.Diagnostics.Debug.WriteLine($"[ExifExtractor] Image EXIF read failed for {file.FileName}: {ex.Message}");
            return null;
        }
    }

    private static DateTimeOffset? ExtractFromVideo(MediaFile file)
    {
        if (file.Source is null) return null;
        try
        {
            var abstraction = new TagLibStreamFileAbstraction(file.FileName, file.Source.OpenRead);
            using var tagFile = TagLib.File.Create(abstraction);
            // 容器标签日期（QuickTime ©day / ID3 TDRC 等）；优先取完整日期，退化到年份兜底
            var dt = tagFile.Tag.DateTagged ?? YearOnly(tagFile.Tag.Year);
            if (dt is { } d)
                return new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Unspecified),
                    TimeZoneInfo.Local.GetUtcOffset(d));
        }
        catch (Exception ex)
        {
            // 容器无法解析等：视为无结果
            System.Diagnostics.Debug.WriteLine($"[ExifExtractor] Video tag read failed for {file.FileName}: {ex.Message}");
        }
        return null;
    }

    private static DateTime? YearOnly(uint year)
        => year is >= 1970 and <= 2100 ? new DateTime((int)year, 1, 1) : null;
}
