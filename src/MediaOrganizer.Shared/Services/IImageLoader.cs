using Avalonia.Media.Imaging;

namespace MediaOrganizer.Shared.Services;

/// <summary>
/// 图片预览抽象：桌面 ThumbnailService（Magick.NET 解码 + LRU 缓存）；
/// Android 首版无缩略图预览（SRS FR-A6.2，后续迭代），实现返回 null。
/// </summary>
public interface IImageLoader
{
    Bitmap? LoadThumbnail(string identifier, int maxSize);
}
