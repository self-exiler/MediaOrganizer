using System.Diagnostics;
using Avalonia.Media.Imaging;
using ImageMagick;

namespace MediaOrganizer.Desktop.Services;

/// <summary>缩略图服务：Magick.NET 解码（含 HEIC）→ PNG → Avalonia Bitmap。</summary>
public static class ThumbnailService
{
    public static Bitmap? Load(string path, int maxSize = 320)
    {
        try
        {
            using var image = new MagickImage(path);
            if (image.Width > maxSize || image.Height > maxSize)
                image.Thumbnail(new MagickGeometry((uint)maxSize) { IgnoreAspectRatio = false });
            image.Format = MagickFormat.Png;

            var ms = new MemoryStream();
            image.Write(ms, MagickFormat.Png);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch
        {
            return null; // 非图片/损坏/视频等
        }
    }

    public static void OpenWithSystemApp(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // 忽略打开失败
        }
    }
}
