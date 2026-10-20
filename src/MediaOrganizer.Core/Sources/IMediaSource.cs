namespace MediaOrganizer.Core.Sources;

/// <summary>
/// 源端抽象：所有"读源文件"的位置统一走它。
/// 双端均为本地路径实现（LocalMediaSource）；Android 的 SAF 回退已删除，content: URI 仅作为历史标识符出现。
/// </summary>
public interface IMediaSource
{
    /// <summary>源标识符：本地绝对路径（或历史遗留的 content:// URI）。</summary>
    string Identifier { get; }

    /// <summary>文件名。</summary>
    string DisplayName { get; }

    long Length { get; }

    /// <summary>修改时间（扫描时预取）。</summary>
    DateTimeOffset? ModifiedTime { get; }

    /// <summary>打开只读流。</summary>
    Stream OpenRead();

    /// <summary>move 语义删除源。</summary>
    void Delete();
}
