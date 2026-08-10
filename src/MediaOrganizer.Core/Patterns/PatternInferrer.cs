using System.Text;
using System.Text.RegularExpressions;
using MediaOrganizer.Core.Configuration;

namespace MediaOrganizer.Core.Patterns;

/// <summary>标记角色（FR-7.3 交互圈选）。</summary>
public enum MarkRole { None, Year, Month, Day, Hour, Minute, Second, Timestamp, Ignore, Required }

/// <summary>文件名中的一个字符及其圈选标记。</summary>
public sealed record CharCell(char Char, MarkRole Role);

/// <summary>
/// 从正则表达式 + 真实文件名样本推断 PatternDefinition（组映射/时间戳长度）；
/// 以及 FR-7.3/7.4：从交互圈选标记生成正则、多变体智能生成。
/// 原实现住在 GUI 层且用假探测串 "probe12345678901234567890"——无法命中真实样本中的日期片段，
/// 导致命名组探测失败后靠位置武断推断。本推断器用样本本身探测，并优先采纳命名组。
/// </summary>
public static class PatternInferrer
{
    /// <summary>用第一个样本探测组结构并生成 PatternDefinition；正则非法返回 null。</summary>
    public static PatternDefinition? Infer(string regex, IReadOnlyList<string> samples)
    {
        try
        {
            var probe = new PatternDefinition
            {
                Name = "__test__",
                Pattern = regex,
                Enabled = true,
                GroupMapping = new Dictionary<string, int>()
            };

            var sample = samples.FirstOrDefault() ?? "";
            var m = Regex.Match(sample, regex, RegexOptions.CultureInvariant);
            if (!m.Success) return probe; // 允许零命中（测试显示未命中）

            var map = new Dictionary<string, int>();
            // 命名组优先：year/month/day/hour/minute/second/timestamp
            for (var i = 1; i < m.Groups.Count; i++)
            {
                var name = m.Groups[i].Name;
                if (name != i.ToString() && !string.IsNullOrEmpty(name))
                    map[name] = i;
            }

            if (map.Count == 0)
            {
                // 位置推断：1 组纯数字 → 时间戳（按长度定 10/13/16）；3 组 → y/m/d；6 组 → y/m/d/h/m/s
                var n = m.Groups.Count - 1;
                if (n == 1 && long.TryParse(m.Groups[1].Value, out var ts))
                {
                    var len = m.Groups[1].Value.Length;
                    if (len is 10 or 13 or 16)
                    {
                        map["timestamp"] = 1;
                        probe.TimestampLength = len;
                    }
                }
                else if (n >= 3)
                {
                    map["year"] = 1; map["month"] = 2; map["day"] = 3;
                    if (n >= 6) { map["hour"] = 4; map["minute"] = 5; map["second"] = 6; }
                }
            }

            probe.GroupMapping = map;
            return probe;
        }
        catch
        {
            return null; // 正则非法
        }
    }

    /// <summary>把文件名渲染成字符单元（FR-7.2 字符着色视图）。</summary>
    public static IReadOnlyList<CharCell> ToCharCells(string fileName)
        => fileName.Select(c => new CharCell(c, MarkRole.None)).ToArray();

    /// <summary>
    /// FR-7.3：从交互圈选标记生成正则与 group_mapping。
    /// 相邻同类标记合并为捕获组；数字区间生成 \d{n}，其他字符原样转义。
    /// 返回 null 表示没有可生成的日期标记。
    /// </summary>
    public static PatternDefinition? GenerateFromMarks(string fileName, IReadOnlyList<CharCell> cells)
    {
        if (cells.Count == 0) return null;

        var sb = new StringBuilder();
        var map = new Dictionary<string, int>();
        var ignored = new List<int>();
        var group = 0;

        var i = 0;
        while (i < cells.Count)
        {
            var role = cells[i].Role;
            if (role == MarkRole.None)
            {
                sb.Append(Regex.Escape(cells[i].Char.ToString()));
                i++;
                continue;
            }

            // 收集同角色连续段
            var start = i;
            while (i < cells.Count && cells[i].Role == role) i++;
            var segment = new string(cells.Skip(start).Take(i - start).Select(c => c.Char).ToArray());
            group++;

            if (role == MarkRole.Ignore)
            {
                ignored.Add(group);
                sb.Append("(.*)");
                continue;
            }

            if (role == MarkRole.Required)
            {
                sb.Append($"({Regex.Escape(segment)})");
                continue;
            }

            if (role == MarkRole.Timestamp)
            {
                sb.Append($"(\\d{{{segment.Length}}})");
                map["timestamp"] = group;
            }
            else
            {
                sb.Append($"(\\d{{{segment.Length}}})");
                map[RoleKey(role)] = group;
            }
        }

        if (map.Count == 0) return null;

        var pattern = new PatternDefinition
        {
            Name = "__test__",
            Pattern = sb.ToString(),
            Enabled = true,
            GroupMapping = map,
            IgnoredGroups = ignored
        };
        if (map.TryGetValue("timestamp", out var tsGroup))
            pattern.TimestampLength = cells.Where(c => c.Role == MarkRole.Timestamp).Count();
        return pattern;
    }

    private static string RoleKey(MarkRole role) => role switch
    {
        MarkRole.Year => "year",
        MarkRole.Month => "month",
        MarkRole.Day => "day",
        MarkRole.Hour => "hour",
        MarkRole.Minute => "minute",
        MarkRole.Second => "second",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    /// <summary>
    /// FR-7.4：多变体智能生成——对比多个同指纹样本，把差异部分生成 \d{n} 或通配，
    /// 产出覆盖全部样本的正则（取最长公共结构，差异位一律数字通配）。
    /// </summary>
    public static string GenerateVariantRegex(IReadOnlyList<string> samples)
    {
        if (samples.Count == 0) return "";
        if (samples.Count == 1)
        {
            // 单样本：按字符类型转义（数字→\d{1}，其余转义）
            return Regex.Escape(samples[0]);
        }

        // 逐位比较：全部一致 → 转义字符；任一不一致 → 若该位全为数字则 \d{1}，否则 .
        var minLen = samples.Min(s => s.Length);
        var sb = new StringBuilder();
        for (var pos = 0; pos < minLen; pos++)
        {
            var chars = samples.Select(s => s[pos]).ToArray();
            if (chars.Distinct().Count() == 1)
            {
                sb.Append(Regex.Escape(chars[0].ToString()));
            }
            else if (chars.All(char.IsDigit))
            {
                sb.Append(@"\d{1}");
            }
            else
            {
                sb.Append('.');
            }
        }
        return sb.ToString();
    }
}
