using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Extraction;
using MediaOrganizer.Core.Scanning;

namespace MediaOrganizer.Core;

/// <summary>服务组合工厂：GUI/CLI 只需在此拼装一次，配置变化后重建即可。</summary>
public static class CoreFactory
{
    public static FileScanner CreateScanner(AppConfig config)
        => FileScanner.FromConfig(config);

    public static ExtractorChain CreateChain(AppConfig config, IReadOnlyList<PatternDefinition> patterns)
        => ExtractorChain.FromConfig(config.Extraction, patterns);

    public static Analyzer CreateAnalyzer(AppConfig config, IReadOnlyList<PatternDefinition> patterns)
        => new(CreateScanner(config), CreateChain(config, patterns),
            config.Scan.MaxDegreeOfParallelism, config.Scan.ProgressInterval);
}
