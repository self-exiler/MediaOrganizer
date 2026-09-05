using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Core.Analysis;

/// <summary>分析结果的 JSON 持久化（analysis-result.json，结构见 SRS §5.2）。
/// 注意：MediaFile.Source（源端流抽象）不参与序列化（P2-5），回读结果仅供展示/取文件名/喂给计划重建；
/// 若真要跨会话执行，调用方须按 Path 重建 Source，否则 FileOperator 会报错。
/// 故注释不再声称"支持跨会话恢复执行"。</summary>
public static class AnalysisResultStore
{
    private sealed class ResultFile
    {
        public int Version { get; set; } = 1;
        public string SourceDir { get; set; } = "";
        public DateTimeOffset AnalyzedAt { get; set; }
        public List<ParsedEntry> Parsed { get; set; } = [];
        public List<UnparsedEntry> Unparsed { get; set; } = [];
    }

    private sealed class ParsedEntry
    {
        public string Path { get; set; } = "";
        public DateTimeOffset Date { get; set; }
        public string Source { get; set; } = "";
        public long Size { get; set; }
    }

    private sealed class UnparsedEntry
    {
        public string Path { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    public static void Save(string path, AnalysisResult result)
    {
        var payload = new ResultFile
        {
            SourceDir = result.SourceDir,
            AnalyzedAt = result.AnalyzedAt,
            Parsed = result.Parsed.Select(p => new ParsedEntry
            {
                Path = p.File.Path,
                Date = p.Date,
                Source = p.Source,
                Size = p.File.Size
            }).ToList(),
            Unparsed = result.Unparsed.Select(u => new UnparsedEntry
            {
                Path = u.File.Path,
                Reason = u.Reason
            }).ToList()
        };
        JsonFileStore.Save(path, payload);
    }

    /// <summary>从落盘 JSON 恢复分析结果；文件缺失/损坏返回 null。</summary>
    public static AnalysisResult? Load(string path)
    {
        var file = JsonFileStore.Load<ResultFile>(path);
        if (file is null) return null;

        var parsed = file.Parsed
            .Select(p => new ParsedFile(new MediaFile(p.Path, p.Size, ExtensionOf(p.Path)), p.Date, p.Source))
            .ToArray();
        var unparsed = file.Unparsed
            .Select(u => new UnparsedFile(new MediaFile(u.Path, 0, ExtensionOf(u.Path)), u.Reason))
            .ToArray();
        return new AnalysisResult(file.SourceDir, file.AnalyzedAt, parsed, unparsed);
    }

    private static string ExtensionOf(string path)
        => System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
}
