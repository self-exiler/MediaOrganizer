namespace MediaOrganizer.Core.Sources;

/// <summary>
/// 源端抽象（ADR-0006 决策 1）：所有"读源文件"的位置统一走它。
/// 桌面为本地路径（LocalMediaSource），Android 为 SAF content URI（AndroidSafMediaSource）。
/// </summary>
public interface IMediaSource
{
    /// <summary>源标识符：本地绝对路径或 content:// URI。</summary>
    string Identifier { get; }

    /// <summary>文件名（SAF 下取 DocumentFile.Name；本地为文件系统名）。</summary>
    string DisplayName { get; }

    long Length { get; }

    /// <summary>修改时间（SAF 下扫描时自 DocumentFile.lastModified() 预取）。</summary>
    DateTimeOffset? ModifiedTime { get; }

    /// <summary>打开只读流（本地 FileStream / Android ContentResolver 流）。</summary>
    Stream OpenRead();

    /// <summary>move 语义删除源。</summary>
    void Delete();
}
