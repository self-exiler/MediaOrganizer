using ImageMagick;
using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Core.Extraction;

/// <summary>EXIF 提取器：图片用 Magick.NET 读 EXIF（jpg/png/tiff/webp/heic），视频用 TagLib 读容器元数据（FR-2.3）。</summary>
public sealed class ExifExtractor(bool enabled, double weight) : IDateExtractor
{
    public string Name => "Exif";
    public bool Enabled { get; } = enabled;
    public double Weight { get; } = weight;

    // 按 EXIF 规范优先级：DateTimeOriginal → DateTimeDigitized → DateTime
    // 注：Magick.NET 14 中这些标签为 ExifTag<string>（值形如 "yyyy:MM:dd HH:mm:ss"）
    private static readonly ExifTag<string>[] DateTags =
        [ExifTag.DateTimeOriginal, ExifTag.DateTimeDigitized, ExifTag.DateTime];

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp", ".3g2"
    };

    public DateTimeOffset? Extract(MediaFile file)
        => VideoExtensions.Contains(Path.GetExtension(file.Path))
            ? ExtractFromVideo(file.Path)
            : ExtractFromImage(file.Path);

    private static DateTimeOffset? ExtractFromImage(string path)
    {
        try
        {
            using var image = new MagickImage();
            image.Ping(path);
            var profile = image.GetExifProfile();
            if (profile is null) return null;

            foreach (var tag in DateTags)
            {
                var value = profile.GetValue(tag);
                if (value?.Value is string s && TryParseExifString(s, out var dt))
                    return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
            }

            // 兼容性兜底：部分文件 EXIF 值以字符串属性存储
            foreach (var tag in DateTags)
            {
                var attr = image.GetAttribute($"exif:{tag}");
                if (attr is not null && TryParseExifString(attr, out var dt2))
                    return new DateTimeOffset(dt2, TimeZoneInfo.Local.GetUtcOffset(dt2));
            }
        }
        catch
        {
            // 损坏文件/无权限等：一律视为该提取器无结果
        }
        return null;
    }

    private static DateTimeOffset? ExtractFromVideo(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            // 容器标签日期（QuickTime ©day / ID3 TDRC 等）；优先取完整日期，退化到年份兜底
            var dt = file.Tag.DateTagged ?? YearOnly(file.Tag.Year);
            if (dt is { } d)
                return new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Unspecified),
                    TimeZoneInfo.Local.GetUtcOffset(d));
        }
        catch
        {
            // 容器无法解析等：视为无结果
        }
        return null;
    }

    private static DateTime? YearOnly(uint year)
        => year is >= 1970 and <= 2100 ? new DateTime((int)year, 1, 1) : null;

    private static bool TryParseExifString(string value, out DateTime dt)
    {
        // EXIF 日期格式 "yyyy:MM:dd HH:mm:ss"
        if (DateTime.TryParseExact(value.Trim(), "yyyy:MM:dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt))
            return true;
        // 兼容 "yyyy:MM:dd"
        return DateTime.TryParseExact(value.Trim(), "yyyy:MM:dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out dt);
    }
}
