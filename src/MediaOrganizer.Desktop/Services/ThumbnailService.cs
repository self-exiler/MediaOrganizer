using Avalonia.Media.Imaging;
using ImageMagick;

namespace MediaOrganizer.Desktop.Services;

/// <summary>
/// 缩略图服务：Magick.NET 解码（含 HEIC）→ PNG → Avalonia Bitmap，调用方负责 Dispose。
/// 不做缓存：命中率低（预览逐张翻看），且缓存 PNG 字节命中后仍要重新解码成 Bitmap，省不掉主要开销。
/// </summary>
public static class ThumbnailService
{
    public static Bitmap? Load(string path, int maxSize = 320)
    {
        byte[] png;
        try
        {
            using var image = new MagickImage(path);
            // 手机照片多带 EXIF 方向标记：不 AutoOrient 直接缩放，竖拍图会以横向拉伸的样子出现在预览里
            image.AutoOrient();
            if (image.Width > maxSize || image.Height > maxSize)
                image.Thumbnail(new MagickGeometry((uint)maxSize) { IgnoreAspectRatio = false });
            using var ms = new MemoryStream();
            image.Write(ms, MagickFormat.Png);
            png = ms.ToArray();
        }
        catch
        {
            return null; // 非图片/损坏/视频等
        }

        try
        {
            using var bmpStream = new MemoryStream(png);
            return new Bitmap(bmpStream);
        }
        catch
        {
            return null;
        }
    }
}
