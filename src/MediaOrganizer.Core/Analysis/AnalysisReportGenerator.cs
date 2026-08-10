using System.Text;
using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Core.Analysis;

/// <summary>生成 TXT 分析报告（SRS FR-4.3）。</summary>
public static class AnalysisReportGenerator
{
    public static string Generate(AnalysisResult result, string outputDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("媒体文件分析报告");
        sb.AppendLine("==================");
        sb.AppendLine($"源目录:    {result.SourceDir}");
        sb.AppendLine($"输出目录:  {outputDir}");
        sb.AppendLine($"分析时间:  {result.AnalyzedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"总计: {result.Total}  成功: {result.Parsed.Count} ({result.SuccessRate:P1})  失败: {result.Unparsed.Count}");
        sb.AppendLine();

        sb.AppendLine("按来源:");
        foreach (var group in result.Parsed.GroupBy(p => p.Source).OrderByDescending(g => g.Count()))
            sb.AppendLine($"  {group.Key,-12} {group.Count(),8}  ({(double)group.Count() / result.Total:P1})");
        sb.AppendLine();

        sb.AppendLine("按年份:");
        foreach (var group in result.Parsed.GroupBy(p => p.Date.Year).OrderByDescending(g => g.Key))
        {
            var bar = new string('█', Math.Clamp(group.Count() * 20 / Math.Max(1, result.Parsed.Count), 1, 20));
            sb.AppendLine($"  {group.Key}  {bar} {group.Count(),6}");
        }
        sb.AppendLine();

        sb.AppendLine("按月分布:");
        foreach (var group in result.Parsed.GroupBy(p => (p.Date.Year, p.Date.Month)).OrderByDescending(g => g.Key))
        {
            var bar = new string('█', Math.Clamp(group.Count() * 20 / Math.Max(1, result.Parsed.Count), 1, 20));
            sb.AppendLine($"  {group.Key.Year:0000}-{group.Key.Month:00}  {bar} {group.Count(),6}");
        }
        sb.AppendLine();

        sb.AppendLine($"失败文件（{result.Unparsed.Count}）:");
        foreach (var f in result.Unparsed.Take(200))
            sb.AppendLine($"  {f.File.FileName}   {f.Reason}");
        if (result.Unparsed.Count > 200)
            sb.AppendLine($"  ... 其余 {result.Unparsed.Count - 200} 个略");
        return sb.ToString();
    }
}
