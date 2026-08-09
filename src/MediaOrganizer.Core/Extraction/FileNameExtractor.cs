using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Patterns;

namespace MediaOrganizer.Core.Extraction;

/// <summary>文件名提取器：用正则模式集合解析文件名中的日期。</summary>
public sealed class FileNameExtractor(IReadOnlyList<Configuration.PatternDefinition> patterns, bool enabled, double weight) : IDateExtractor
{
    public string Name => "FileName";
    public bool Enabled { get; } = enabled;
    public double Weight { get; } = weight;

    public DateTimeOffset? Extract(MediaFile file)
    {
        // 快速年份预扫描：文件名无合理年份且无时间戳模式时直接跳过全部正则（SRS FR-3.3）
        if (!PatternEngine.ContainsLikelyDate(file.FileName, patterns))
            return null;
        return PatternEngine.TryExtract(file.FileName, patterns);
    }
}
