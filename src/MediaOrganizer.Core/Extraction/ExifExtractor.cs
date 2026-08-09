using ImageMagick;
using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Core.Extraction;

/// <summary>EXIF 提取器：用 Magick.NET 统一读取 jpg/png/tiff/webp/heic 的 EXIF 日期。</summary>
public sealed class ExifExtractor(bool enabled, double weight) : IDateExtractor
{
    public string Name => "Exif";
    public bool Enabled { get; } = enabled;
    public double Weight { get; } = weight;

    // 按 EXIF 规范优先级：DateTimeOriginal → DateTimeDigitized → DateTime
    // 注：Magick.NET 14 中这些标签为 ExifTag<string>（值形如 "yyyy:MM:dd HH:mm:ss"）
    private static readonly ExifTag<string>[] DateTags =
        [ExifTag.DateTimeOriginal, ExifTag.DateTimeDigitized, ExifTag.DateTime];

    public DateTimeOffset? Extract(MediaFile file)
    {
        try
        {
            using var image = new MagickImage(file.Path);
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
            // 视频/损坏文件/无权限等：一律视为该提取器无结果
        }
        return null;
    }

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
