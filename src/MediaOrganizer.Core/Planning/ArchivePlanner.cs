using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Core.Planning;

/// <summary>归档计划中的单个文件。</summary>
public sealed record PlannedFile(MediaFile Source, DateTimeOffset Date, string RelativeTarget);

/// <summary>一次完整归档计划。</summary>
public sealed record ArchivePlan(
    AnalysisResult Result,
    string OutputRoot,
    ClassificationLevel Level,
    IReadOnlyList<PlannedFile> Files);

/// <summary>规划器：日期 → 分级目标路径，未来日期归入 FutureDate/（SRS FR-5.1 / FR-5.4）。</summary>
/// <param name="level">目录分级。</param>
/// <param name="futureDateBufferDays">未来缓冲天数，与 DateRangeValidator 同标准（FR-5.4/FR-2.4 一致）。</param>
/// <param name="now">基准时间（测试注入）。</param>
public sealed class ArchivePlanner(ClassificationLevel level, int futureDateBufferDays = 0, DateTimeOffset? now = null)
{
    public ArchivePlan Plan(AnalysisResult result, string outputRoot)
    {
        var n = now ?? DateTimeOffset.Now;
        var files = new List<PlannedFile>(result.Parsed.Count);
        foreach (var parsed in result.Parsed)
        {
            var rel = BuildRelative(parsed.Date, n);
            // 计划内统一使用 '/' 分隔，便于跨平台测试；执行时再按平台转换
            files.Add(new PlannedFile(parsed.File, parsed.Date, $"{rel}/{parsed.File.FileName}"));
        }
        return new ArchivePlan(result, outputRoot, level, files);
    }

    private string BuildRelative(DateTimeOffset date, DateTimeOffset now)
    {
        var sub = level switch
        {
            ClassificationLevel.Year => $"{date.Year:0000}",
            ClassificationLevel.Month => $"{date.Year:0000}/{date.Month:00}",
            _ => $"{date.Year:0000}/{date.Month:00}/{date.Day:00}"
        };
        // 与校验器一致：仅超出 now+缓冲 的日期归入 FutureDate/
        return date.Date > now.Date.AddDays(futureDateBufferDays) ? $"FutureDate/{sub}" : sub;
    }
}
