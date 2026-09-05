namespace MediaOrganizer.Core.Extraction;

/// <summary>
/// EXIF 日期字符串解析（双端共享，ADR-0006 决策 2）：
/// 桌面 MagickExifReader 与 Android AndroidExifReader 读取的 EXIF 日期值格式相同，解析规则统一在此。
/// </summary>
public static class ExifDateParser
{
    /// <summary>EXIF 日期格式 "yyyy:MM:dd HH:mm:ss"，退化兼容 "yyyy:MM:dd"。</summary>
    public static bool TryParse(string value, out DateTime dt)
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
