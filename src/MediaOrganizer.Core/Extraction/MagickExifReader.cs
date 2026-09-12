#if !ANDROID
using ImageMagick;
using MediaOrganizer.Core.Platforms;

namespace MediaOrganizer.Core.Extraction;

/// <summary>
/// 桌面版图片 EXIF 读取器（Magick.NET，走流）：ExifExtractor 经 IExifReader 抽象调用（ADR-0006 决策 2）。
/// Android 侧由 AndroidExifReader（ExifInterface）替换，提取器逻辑双端共享。
/// </summary>
public sealed class MagickExifReader : IExifReader
{
    // 按 EXIF 规范优先级：DateTimeOriginal → DateTimeDigitized → DateTime
    // 注：Magick.NET 14 中这些标签为 ExifTag<string>（值形如 "yyyy:MM:dd HH:mm:ss"）
    private static readonly ExifTag<string>[] DateTags =
        [ExifTag.DateTimeOriginal, ExifTag.DateTimeDigitized, ExifTag.DateTime];

    public DateTimeOffset? ReadImageExif(Stream stream)
    {
        try
        {
            using var image = new MagickImage();
            image.Ping(stream);
            var profile = image.GetExifProfile();
            if (profile is null) return null;

            // 先读 EXIF profile 值，缺失时兜底字符串属性（部分文件 EXIF 值以属性存储），一趟完成
            foreach (var tag in DateTags)
            {
                var s = profile.GetValue(tag)?.Value ?? image.GetAttribute($"exif:{tag}");
                if (s is not null && ExifDateParser.TryParse(s, out var dt))
                    return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
            }
        }
        catch (Exception ex)
        {
            // 损坏文件/无权限等：一律视为该提取器无结果
            System.Diagnostics.Debug.WriteLine($"[MagickExifReader] EXIF read failed: {ex.Message}");
        }
        return null;
    }
}
#endif
