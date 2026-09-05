using Avalonia.Media.Imaging;
using ImageMagick;

namespace MediaOrganizer.Desktop.Services;

/// <summary>
/// 缩略图服务：Magick.NET 解码（含 HEIC）→ PNG 字节 → Avalonia Bitmap，内置 LRU 缓存。
/// 缓存持有 PNG 字节而非 Bitmap 实例：每次调用新建 Bitmap 由调用方 Dispose，
/// 避免缓存共享实例被单方释放（评审 2.14：原生位图内存持续增长）。
/// </summary>
public static class ThumbnailService
{
    private static readonly LinkedList<(string Path, byte[] Png)> _lruList = new();
    private static readonly Dictionary<string, LinkedListNode<(string Path, byte[] Png)>> _lruMap = new();
    private static readonly object _lock = new();
    private const int MaxCacheSize = 20;

    public static Bitmap? Load(string path, int maxSize = 320)
    {
        // P3-5：缓存键需含 maxSize，否则用户在设置改预览尺寸后仍会命中旧尺寸缩略图
        var key = $"{path}|{maxSize}";
        byte[]? png;
        lock (_lock)
        {
            png = _lruMap.TryGetValue(key, out var node) ? Touch(node) : null;
        }

        if (png is null)
        {
            try
            {
                using var image = new MagickImage(path);
                if (image.Width > maxSize || image.Height > maxSize)
                    image.Thumbnail(new MagickGeometry((uint)maxSize) { IgnoreAspectRatio = false });
                using var ms = new MemoryStream();
                image.Write(ms, MagickFormat.Png);
                png = ms.ToArray();

                lock (_lock)
                {
                    // 双重检查，避免并发加载同一文件；后到者采用先到者的缓存副本保持唯一性
                    if (_lruMap.TryGetValue(key, out var existing))
                    {
                        png = Touch(existing);
                    }
                    else
                    {
                        _lruMap[key] = _lruList.AddFirst((key, png));
                        while (_lruList.Count > MaxCacheSize)
                        {
                            var last = _lruList.Last!;
                            _lruMap.Remove(last.Value.Path);
                            _lruList.RemoveLast();
                        }
                    }
                }
            }
            catch
            {
                return null; // 非图片/损坏/视频等
            }
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

    private static byte[] Touch(LinkedListNode<(string Path, byte[] Png)> node)
    {
        _lruList.Remove(node);
        _lruList.AddFirst(node);
        return node.Value.Png;
    }
}
