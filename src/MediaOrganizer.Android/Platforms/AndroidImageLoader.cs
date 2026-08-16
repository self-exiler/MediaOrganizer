using Avalonia.Media.Imaging;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// Android 图片预览（SRS FR-A6.2：首版无缩略图预览，后续迭代）→ 返回 null。
/// 接口占位，保证共享 FailedFilesViewModel 双端可编译。
/// </summary>
public sealed class AndroidImageLoader : IImageLoader
{
    public Bitmap? LoadThumbnail(string identifier, int maxSize) => null;
}
