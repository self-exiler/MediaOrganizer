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
            // 命名组优先，但只采纳语义白名单内的组名（P2-3）：
            // 否则诸如 (?<foo>…) 的任意命名组会作出 map.Count != 0 让位置推断被跳过，而该规则永远解析不出日期。
            var whitelist = new HashSet<string>(StringComparer.Ordinal)
                { "year", "month", "day", "hour", "minute", "second", "timestamp" };
            for (var i = 1; i < m.Groups.Count; i++)
            {
                var name = m.Groups[i].Name;
                if (!string.IsNullOrEmpty(name) && whitelist.Contains(name))
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
            // 命名组 timestamp 时 TimestampLength 也须给出，否则 TimestampToDate 按秒解析 13 位毫秒时间戳会越界返回 null（P0-3 附带缺口）
            if (map.TryGetValue("timestamp", out var tsIdx) && m.Groups[tsIdx].Success)
            {
                var len = m.Groups[tsIdx].Value.Length;
                if (len is 10 or 13 or 16) probe.TimestampLength = len;
            }
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
    /// 与参考实现 magic_tools.py 的 _generate_improved_regex_pattern 一致：
    ///  - 未标记的间隙用 (?:.*) 非捕获通配（Python 用捕获组 (.*)，此处用非捕获以保持组号正确）
    ///  - 日期/时间戳段 → (\d{n})；必现段 → (转义文本)；忽略段 → (.*)
    ///  - 以 ^...$ 锚定整条
    /// 返回 null 表示没有可生成的日期标记。
    /// </summary>
    public static PatternDefinition? GenerateFromMarks(string fileName, IReadOnlyList<CharCell> cells)
    {
        if (cells.Count == 0) return null;

        // 收集所有已标记段（同角色连续合并）
        var segments = new List<(int Start, int End, MarkRole Role)>();
        var i = 0;
        while (i < cells.Count)
        {
            if (cells[i].Role == MarkRole.None) { i++; continue; }
            var role = cells[i].Role;
            var start = i;
            while (i < cells.Count && cells[i].Role == role) i++;
            segments.Add((start, i, role));
        }

        if (segments.Count == 0) return null;

        var sb = new StringBuilder();
        sb.Append('^');

        var map = new Dictionary<string, int>();
        var ignored = new List<int>();
        var group = 0;
        var currentPos = 0;

        foreach (var (start, end, role) in segments)
        {
            // 间隙 → (?:.*) 非捕获通配（不占用组号，保持 group_mapping 正确）
            if (start > currentPos)
                sb.Append("(?:.*)");

            var segment = new string(cells.Skip(start).Take(end - start).Select(c => c.Char).ToArray());
            group++;

            if (role == MarkRole.Ignore)
            {
                ignored.Add(group);
                sb.Append("(.*)");
            }
            else if (role == MarkRole.Required)
            {
                sb.Append($"({Regex.Escape(segment)})");
            }
            else if (role == MarkRole.Timestamp)
            {
                sb.Append($"(\\d{{{segment.Length}}})");
                map["timestamp"] = group;
            }
            else
            {
                sb.Append($"(\\d{{{segment.Length}}})");
                map[RoleKey(role)] = group;
            }

            currentPos = end;
        }

        // 尾部间隙 → (?:.*) 容忍扩展名等后缀
        if (currentPos < cells.Count)
            sb.Append("(?:.*)");

        sb.Append('$');

        var pattern = new PatternDefinition
        {
            Name = "__test__",
            Pattern = sb.ToString(),
            Enabled = true,
            GroupMapping = map,
            IgnoredGroups = ignored
        };
        if (map.TryGetValue("timestamp", out _))
            pattern.TimestampLength = cells.Count(c => c.Role == MarkRole.Timestamp);
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
    /// 产出覆盖全部样本的正则并带命名捕获组，使 Infer/TryExtract 能解析出日期（P0-3）。
    /// 修复前逐位输出 \d{1} 不产生任何捕获组，Infer 的 map 恒空 → TryExtract 无 year/timestamp →
    /// 全部样本"未命中"，SavePattern 永远无法通过。
    /// 关键：按"最大连续数字块"切分（而非逐位），避免同数字前缀（如 2023/2024 前三位 "202"）把
    /// 日期字段拆成碎片；块内含差异 → 当作日期/时间戳字段并按长度拆分/命名，块全同 → 折叠为字面量。
    /// </summary>
    public static string GenerateVariantRegex(IReadOnlyList<string> samples)
    {
        if (samples.Count == 0) return "";
        if (samples.Count == 1)
        {
            // 单样本：无法对比差异，整体转义作字面量模板，由用户再手工泛化
            return Regex.Escape(samples[0]);
        }

        var minLen = samples.Min(s => s.Length);
        var n = samples.Count;

        bool AllDigit(int p) { for (int i = 0; i < n; i++) if (!char.IsDigit(samples[i][p])) return false; return true; }
        bool AllSame(int p) { char c = samples[0][p]; for (int i = 1; i < n; i++) if (samples[i][p] != c) return false; return true; }

        // 第一遍：把样本结构规约成有序 token
        //  字面量 → (IsDigitRun=false, Len=0, Lit)；可变数字段 → (true, len, "")；可变非数字段 → (false, 0, '.'×len)
        var parts = new List<(bool IsDigitRun, int Len, string Lit)>();

        // 把 [p0, end) 区间内 samples[0] 的连续相同字符折叠为字面量 token（连续相同用 {n} 量化）
        void FoldLiteralRun(int p0, int end)
        {
            while (p0 < end)
            {
                char c = samples[0][p0];
                int r0 = p0;
                while (r0 < end && samples[0][r0] == c) r0++;
                int len = r0 - p0;
                parts.Add((false, 0, len > 1 ? Regex.Escape(c.ToString()) + "{" + len + "}" : Regex.Escape(c.ToString())));
                p0 = r0;
            }
        }

        int pos = 0;
        while (pos < minLen)
        {
            if (AllDigit(pos))
            {
                // 最大连续数字块（各样本全部数字）→ 取整块，避免同数字前缀拆碎字段
                int end = pos;
                while (end < minLen && AllDigit(end)) end++;

                bool variable = false;
                for (int p = pos; p < end; p++) if (!AllSame(p)) { variable = true; break; }

                if (variable)
                {
                    parts.Add((true, end - pos, ""));
                }
                else
                {
                    // 全同数字块：折叠为字面量
                    FoldLiteralRun(pos, end);
                }
                pos = end;
            }
            else if (AllSame(pos))
            {
                // 非数字字面量 run
                int r = pos;
                while (r < minLen && AllSame(r) && !AllDigit(r)) r++;
                FoldLiteralRun(pos, r);
                pos = r;
            }
            else
            {
                // 可变非数字段（分隔符差异等）：折叠为 '.' 通配
                int r = pos;
                while (r < minLen && !AllDigit(r) && !AllSame(r)) r++;
                parts.Add((false, 0, new string('.', r - pos)));
                pos = r;
            }
        }

        var digitLens = parts.Where(p => p.IsDigitRun).Select(p => p.Len).ToArray();
        var frags = DigitFragments(digitLens);

        var sb = new StringBuilder();
        int di = 0;
        foreach (var part in parts)
            sb.Append(part.IsDigitRun ? frags[di++] : part.Lit);
        return sb.ToString();
    }

    /// <summary>按数字段长度序列生成正则片段（命名捕获组；每个数字段恰好对应一个片段，片段可内含多组）。</summary>
    private static IReadOnlyList<string> DigitFragments(IReadOnlyList<int> lens)
    {
        // 精确日期分栏（分隔符已分离成独立 token）：2024_01_15 → [4,2,2]、2024-01-15_12-30-40 → [4,2,2,2,2,2]
        var seq = DateFieldNames(lens);
        if (seq is not null && seq.Length == lens.Count)
            return lens.Select((len, i) => Named(len, seq[i])).ToArray();

        // 逐段按字段长度拆分/命名（一段可展开为多个命名组）
        var frags = new List<string>();
        foreach (var len in lens)
        {
            if (len == 8) frags.Add("(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})");               // YYYYMMDD
            else if (len == 14) frags.Add("(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})(?<hour>\\d{2})(?<minute>\\d{2})(?<second>\\d{2})"); // YYYYMMDDHHmmss
            else if (len == 6) frags.Add("(?<hour>\\d{2})(?<minute>\\d{2})(?<second>\\d{2})");     // HHmmss
            else frags.Add(Named(len, SingleField(len)));
        }
        return frags;
    }

    private static string[]? DateFieldNames(IReadOnlyList<int> lens)
    {
        if (lens.SequenceEqual([4, 2, 2])) return ["year", "month", "day"];
        if (lens.SequenceEqual([4, 2, 2, 2, 2, 2])) return ["year", "month", "day", "hour", "minute", "second"];
        if (lens.SequenceEqual([2, 2, 2])) return ["month", "day", "hour"];
        if (lens.SequenceEqual([1, 2])) return ["year", "month"];
        return null;
    }

    /// <summary>未识别字段的长度的最可能语义；无法判定返回 null（→ 无名捕获组）。</summary>
    private static string? SingleField(int len) => len switch
    {
        4 => "year",
        10 or 13 or 16 => "timestamp",
        _ => null
    };

    private static string Named(int len, string? name)
        => name is null ? $"(\\d{{{len}}})" : $"(?<{name}>\\d{{{len}}})";
}
