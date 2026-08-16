using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Extraction;
using MediaOrganizer.Core.Platforms;
using MediaOrganizer.Core.Scanning;

namespace MediaOrganizer.Core;

/// <summary>服务组合工厂：把 Analyzer 的依赖（扫描器/提取链/并发度/进度间隔）在配置变化后重建。
/// scanner / exifReader 可注入平台实现（Android：AndroidFileScanner / AndroidExifReader），默认本地实现。</summary>
public static class CoreFactory
{
    public static Analyzer CreateAnalyzer(
        AppConfig config,
        IReadOnlyList<PatternDefinition> patterns,
        IFileScanner? scanner = null,
        IExifReader? exifReader = null)
        => new(scanner ?? FileScanner.FromConfig(config),
            ExtractorChain.FromConfig(config.Extraction, patterns, exifReader),
            config.Scan.MaxDegreeOfParallelism, config.Scan.ProgressInterval);
}
