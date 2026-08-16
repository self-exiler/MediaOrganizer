using Android.Media;
using MediaOrganizer.Core.Platforms;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// Android 图片 EXIF 读取器（FR-A2.6）：ExifInterface(Stream) 走流，经 IExifReader 注入提取器。
/// 支持 jpg/jpeg/png/webp/heic/heif；tiff/bmp 不保证（ExifInterface 限制，落入文件名提取器兜底）。
/// 标签优先级与桌面版一致：DateTimeOriginal → DateTimeDigitized → DateTime（DateTime 由 ExifInterface 合成）。
/// </summary>
public sealed class AndroidExifReader : IExifReader
{
    public DateTimeOffset? ReadImageExif(System.IO.Stream stream)
    {
        try
        {
            using var exif = new ExifInterface(stream);
            var raw = exif.GetAttribute(ExifInterface.TagDatetimeOriginal)
                     ?? exif.GetAttribute(ExifInterface.TagDatetimeDigitized)
                     ?? exif.GetAttribute(ExifInterface.TagDatetime);
            if (string.IsNullOrEmpty(raw)) return null;
            if (TryParseExifString(raw.Trim(), out var dt))
                return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
        }
        catch
        {
            // 损坏文件/无权限：视为该提取器无结果
        }
        return null;
    }

    private static bool TryParseExifString(string value, out DateTime dt)
    {
        // EXIF 日期格式 "yyyy:MM:dd HH:mm:ss"
        if (DateTime.TryParseExact(value, "yyyy:MM:dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt))
            return true;
        // 兼容 "yyyy:MM:dd"
        return DateTime.TryParseExact(value, "yyyy:MM:dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out dt);
    }
}