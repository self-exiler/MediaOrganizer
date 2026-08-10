using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Extraction;
using MediaOrganizer.Core.Scanning;

namespace MediaOrganizer.Core;

/// <summary>服务组合工厂：把 Analyzer 的 4 个依赖（扫描器/提取链/并发度/进度间隔）在配置变化后重建。</summary>
public static class CoreFactory
{
    public static Analyzer CreateAnalyzer(AppConfig config, IReadOnlyList<PatternDefinition> patterns)
        => new(FileScanner.FromConfig(config), ExtractorChain.FromConfig(config.Extraction, patterns),
            config.Scan.MaxDegreeOfParallelism, config.Scan.ProgressInterval);
}
