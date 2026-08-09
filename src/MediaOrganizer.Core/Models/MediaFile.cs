namespace MediaOrganizer.Core.Models;

/// <summary>扫描到的一个媒体文件（不包含任何提取结果）。</summary>
public sealed record MediaFile(string Path, long Size, string Extension)
{
    public string FileName => System.IO.Path.GetFileName(Path);
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
