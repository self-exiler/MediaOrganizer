using System.Text.Json;
using MediaOrganizer.Core.Configuration;

namespace MediaOrganizer.Core.Analysis;

/// <summary>分析结果的 JSON 持久化（analysis-result.json，结构见 SRS §5.2）。</summary>
public static class AnalysisResultStore
{
    public static void Save(string path, Models.AnalysisResult result)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var payload = new
        {
            version = 1,
            sourceDir = result.SourceDir,
            analyzedAt = result.AnalyzedAt,
            parsed = result.Parsed.Select(p => new
            {
                path = p.File.Path,
                date = p.Date,
                source = p.Source,
                size = p.File.Size
            }),
            unparsed = result.Unparsed.Select(u => new
            {
                path = u.File.Path,
                reason = u.Reason
            })
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, ConfigManager.JsonOptions));
    }
}
