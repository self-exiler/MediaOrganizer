using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ImageMagick;

namespace MediaOrganizer.Desktop.Services;

/// <summary>缩略图服务：Magick.NET 解码（含 HEIC）→ PNG → Avalonia Bitmap，内置 LRU 缓存。</summary>
public static class ThumbnailService
{
    private static readonly LinkedList<(string Path, Bitmap Bitmap)> _lruList = new();
    private static readonly Dictionary<string, LinkedListNode<(string Path, Bitmap Bitmap)>> _lruMap = new();
    private static readonly object _lock = new();
    private const int MaxCacheSize = 20;

    public static Bitmap? Load(string path, int maxSize = 320)
    {
        lock (_lock)
        {
            if (_lruMap.TryGetValue(path, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                return node.Value.Bitmap;
            }
        }

        try
        {
            using var image = new MagickImage(path);
            if (image.Width > maxSize || image.Height > maxSize)
                image.Thumbnail(new MagickGeometry((uint)maxSize) { IgnoreAspectRatio = false });
            image.Format = MagickFormat.Png;

            var ms = new MemoryStream();
            image.Write(ms, MagickFormat.Png);
            ms.Position = 0;
            var bitmap = new Bitmap(ms);

            lock (_lock)
            {
                // 双重检查，避免并发加载同一文件
                if (!_lruMap.ContainsKey(path))
                {
                    var newNode = _lruList.AddFirst((path, bitmap));
                    _lruMap[path] = newNode;
                    while (_lruList.Count > MaxCacheSize)
                    {
                        var last = _lruList.Last!;
                        _lruMap.Remove(last.Value.Path);
                        _lruList.RemoveLast();
                    }
                }
            }

            return bitmap;
        }
        catch
        {
            return null; // 非图片/损坏/视频等
        }
    }

    /// <summary>用系统默认应用打开文件（跨平台，经由 Avalonia Launcher）。</summary>
    public static async Task OpenWithSystemAppAsync(string path)
    {
        var top = App.MainWindow;
        if (top is null) return;
        try
        {
            await top.Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
        catch
        {
            // 忽略打开失败
        }
    }
}
