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
        // SAF 下 URI 字符串不保证含可读文件名/扩展名 → 统一取源端 DisplayName（ADR-0006 决策 2）
        var name = file.FileName;
        // 快速年份预扫描：文件名无合理年份且无时间戳模式时直接跳过全部正则（SRS FR-3.3）
        if (!PatternEngine.ContainsLikelyDate(name, patterns))
            return null;
        return PatternEngine.TryExtract(name, patterns);
    }
}
