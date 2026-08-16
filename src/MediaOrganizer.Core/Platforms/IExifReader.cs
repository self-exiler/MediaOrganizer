namespace MediaOrganizer.Core.Platforms;

/// <summary>
/// 图片 EXIF 读取抽象（ADR-0005 §8 / ADR-0006 决策 2）：走流而非路径，双端共享提取器逻辑。
/// 桌面实现为 Magick.NET（MagickExifReader），Android 实现为 ExifInterface(Stream)（AndroidExifReader）。
/// </summary>
public interface IExifReader
{
    /// <summary>从图片流读取拍摄时间；无 EXIF 或解析失败返回 null（调用方视为该提取器无结果）。</summary>
    DateTimeOffset? ReadImageExif(Stream stream);
}
