using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Platforms;

namespace MediaOrganizer.Core.Extraction;

/// <summary>
/// EXIF 提取器（FR-A2.1/FR-A2.3）：图片走注入的 IExifReader 读流（桌面 Magick.NET / Android ExifInterface），
/// 视频走 TagLib# 经 StreamFileAbstraction 从 IMediaSource 流读容器元数据——提取器本体双端共享。
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

    /// <summary>视频元数据内存闸门：SAF 等不可 seek 流会被整文件读入
    /// MemoryStream，超过此阈值直接跳过视频 TagLib 提取，交给 FileName 提取器兜底，避免 OOM。</summary>
    private const long MaxVideoBytesForTagRead = 200L * 1024 * 1024;

    private static DateTimeOffset? ExtractFromVideo(MediaFile file)
    {
        if (file.Source is null) return null;
        // 长度不可知（-1）或超大文件：不确定能否安全读完，跳过 TagLib
        var len = file.Source.Length;
        if (len < 0 || len > MaxVideoBytesForTagRead) return null;
        try
        {
            var abstraction = new TagLibStreamFileAbstraction(file.FileName, file.Source.OpenRead);
            using var tagFile = TagLib.File.Create(abstraction);
            // 容器标签日期：TaglibSharp 文档明示 DateTagged =「标签写入时间」（MP4 映射 ©day/UDTA），非拍摄时间。
            // 原 YearOnly 兜底会把只有年份标签的视频固定归到 1 月 1 日（Exif 权重 1.2 又压掉文件名提取）
            // → 删除年份回退，无完整日期时交给文件名/文件系统提取器兜底
            if (tagFile.Tag.DateTagged is { } d)
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
}
