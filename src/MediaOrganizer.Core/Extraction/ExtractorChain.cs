using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Core.Extraction;

/// <summary>加权链：按权重降序依次尝试启用的提取器，取第一个通过校验的结果（SRS FR-2.2）。</summary>
public sealed class ExtractorChain
{
    private readonly DateRangeValidator _validator;
    private readonly IReadOnlyList<IDateExtractor> _extractors;

    public ExtractorChain(IEnumerable<IDateExtractor> extractors, DateRangeValidator validator)
    {
        _validator = validator;
        _extractors = extractors.OrderByDescending(e => e.Weight).ToArray();
    }

    /// <summary>按配置 + 模式集合构建标准三条提取器链。</summary>
    public static ExtractorChain FromConfig(ExtractionConfig cfg, IReadOnlyList<PatternDefinition> patterns, DateTimeOffset? now = null)
    {
        var settings = cfg.Extractors.ToDictionary(s => s.Name, s => s);
        var validator = new DateRangeValidator(cfg.MaxYearsPast, cfg.FutureDateBufferDays, now);

        static (bool Enabled, double Weight) Pick(Dictionary<string, ExtractorSetting> settings, string name, bool defEnabled, double defWeight)
            => settings.TryGetValue(name, out var s)
                ? (s.Enabled, s.Weight)
                : (defEnabled, defWeight);

        var (exifOn, exifW) = Pick(settings, "Exif", true, 1.2);
        var (fnOn, fnW) = Pick(settings, "FileName", true, 1.1);
        var (fsOn, fsW) = Pick(settings, "FileSystem", false, 0.8);

        IDateExtractor[] list =
        [
            new ExifExtractor(exifOn, exifW),
            new FileNameExtractor(patterns, fnOn, fnW),
            new FileSystemExtractor(fsOn, fsW)
        ];
        return new ExtractorChain(list, validator);
    }

    public ExtractResult? TryExtract(MediaFile file)
    {
        foreach (var extractor in _extractors)
        {
            if (!extractor.Enabled) continue;
            var date = extractor.Extract(file);
            if (date is DateTimeOffset dt && _validator.IsValid(dt))
                return new ExtractResult(dt, extractor.Name);
        }
        return null;
    }
}
