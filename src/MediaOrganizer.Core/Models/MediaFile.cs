using System.Text.Json.Serialization;
using MediaOrganizer.Core.Sources;

namespace MediaOrganizer.Core.Models;

/// <summary>
/// 扫描到的一个媒体文件（不包含任何提取结果）。
/// Path 语义为"源标识符"（本地绝对路径或 content:// URI，ADR-0006 决策 1）；
/// Source 携带源端抽象，不参与序列化，读取结果后由 Scanner/Store 按 Path 重新解析。
/// </summary>
public sealed record MediaFile(string Path, long Size, string Extension)
{
    /// <summary>源端抽象（本地路径 / SAF URI）。手工构造（测试/结果回读）时可为 null。</summary>
    [JsonIgnore]
    public IMediaSource? Source { get; init; }

    public string FileName => Source?.DisplayName ?? System.IO.Path.GetFileName(Path);
}

/// <summary>成功提取到日期的文件。</summary>
public sealed record ParsedFile(MediaFile File, DateTimeOffset Date, string Source);

/// <summary>未能提取到日期的文件。</summary>
public sealed record UnparsedFile(MediaFile File, string Reason);

/// <summary>一次完整分析的结果。</summary>
public sealed record AnalysisResult(
    string SourceDir,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<ParsedFile> Parsed,
    IReadOnlyList<UnparsedFile> Unparsed)
{
    public int Total => Parsed.Count + Unparsed.Count;
    public double SuccessRate => Total == 0 ? 0 : (double)Parsed.Count / Total;
}
