using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>Android 首版无缩略图预览（SRS FR-A6.2，后续迭代）：恒返回 null。</summary>
public sealed class AndroidImageLoader : IImageLoader
{
    public Avalonia.Media.Imaging.Bitmap? LoadThumbnail(string identifier, int maxSize) => null;
}
