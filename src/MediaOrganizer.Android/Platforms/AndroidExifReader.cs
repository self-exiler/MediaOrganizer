using MediaOrganizer.Core.Platforms;
// 别名引入避免 using Android.Media（其 Stream 类型与 System.IO.Stream 冲突）
using ExifInterface = Android.Media.ExifInterface;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 图片 EXIF 读取（FR-A2.1/FR-A2.6）：Android.Media.ExifInterface 走流构造，
/// 支持 jpg/jpeg/png/webp/heic/heif；tiff/bmp 不保证（提取器链由 FileName 兜底，R-A1）。
/// 视频不走此类（ExifExtractor 统一走 TagLib# 容器元数据，双端共享）。
/// </summary>
public sealed class AndroidExifReader : IExifReader
{
    // EXIF 规范优先级：DateTimeOriginal → DateTimeDigitized → DateTime
    private static readonly string[] DateTags =
    [
        ExifInterface.TagDatetimeOriginal,
        ExifInterface.TagDatetimeDigitized,
        ExifInterface.TagDatetime
    ];

    public DateTimeOffset? ReadImageExif(Stream stream)
    {
        try
        {
            using var exif = new ExifInterface(stream);
            foreach (var tag in DateTags)
            {
                var value = exif.GetAttribute(tag);
                if (!string.IsNullOrEmpty(value) && TryParseExifString(value, out var dt))
                    return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
            }
        }
        catch
        {
            // 损坏文件/流读取失败：一律视为该提取器无结果
        }
        return null;
    }

    internal static bool TryParseExifString(string value, out DateTime dt)
    {
        value = value.Trim();
        return DateTime.TryParseExact(value, "yyyy:MM:dd HH:mm:ss",
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out dt)
               || DateTime.TryParseExact(value, "yyyy:MM:dd",
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out dt);
    }
}
