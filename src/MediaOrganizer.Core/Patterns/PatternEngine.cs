using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MediaOrganizer.Core.Configuration;

namespace MediaOrganizer.Core.Patterns;

/// <summary>文件名模式引擎：正则匹配 + group_mapping + 时间戳解析。</summary>
public static class PatternEngine
{
    private static readonly TimeZoneInfo LocalTz = TimeZoneInfo.Local;

    // 热路径正则超时：病态回溯在单文件上必须有硬上界，否则并行分析线程被无限占用
    private static readonly Regex TimestampRegex = new(@"\d{10,}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex YearRegex = new(@"19[7-9]\d|20\d\d|21\d\d", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    // 正则编译缓存：pattern string → compiled Regex（线程安全）
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new();

    private static Regex? GetCachedRegex(string pattern)
        => RegexCache.GetOrAdd(pattern, p =>
        {
            try { return new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
            catch { return null; }
        });

    /// <summary>不缓存、不编译的新正则（供魔术工具实时测试）。Compiled 会生成无法卸载的动态程序集，
    /// 逐键入字符地编译会永久泄漏，故仅用于固定 patterns.json 的预编译热路径。
    /// 超时放宽到 2s：用户在 UI 输入 evil regex 时宁可判「未命中」也不冻结界面。</summary>
    private static Regex? NewRegex(string pattern)
    {
        try { return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)); }
        catch { return null; }
    }

    /// <summary>快速预扫描：文件名中是否可能存在日期（年份或 ≥10 位连续数字时间戳），避免对每个文件跑全部正则。</summary>
    public static bool ContainsLikelyDate(string fileName, bool hasTimestampPattern)
    {
        // 存在时间戳模式时：文件名需含 ≥10 位连续数字才算疑似
        if (hasTimestampPattern && TimestampRegex.IsMatch(fileName))
            return true;
        // 4 位年份（1970-2100），允许嵌入更长的数字串（如紧凑日期 20240115）
        // EnumerateMatches 零分配（原先 Matches 分配 MatchCollection + 每个 Match 对象）
        foreach (var m in YearRegex.EnumerateMatches(fileName))
        {
            if (int.TryParse(fileName.Substring(m.Index, m.Length), out var y) && y >= 1970 && y <= 2100) return true;
        }
        return false;
    }

    /// <summary>按单个模式尝试提取（多模式排序过滤由调用方在提取链构造期完成，勿在每文件 LINQ）。</summary>
    public static DateTimeOffset? TryExtract(string fileName, PatternDefinition pattern, bool cache = true)
    {
        if (!pattern.Enabled || string.IsNullOrEmpty(pattern.Pattern)) return null;

        var regex = cache ? GetCachedRegex(pattern.Pattern) : NewRegex(pattern.Pattern);
        if (regex is null) return null;

        Match m;
        try
        {
            m = regex.Match(fileName);
        }
        catch (RegexMatchTimeoutException)
        {
            // 病态正则超时：视为未命中，继续下一模式
            return null;
        }
        if (!m.Success) return null;

        var mapping = pattern.GroupMapping ?? new Dictionary<string, int>();
        var ignored = pattern.IgnoredGroups ?? [];

        // 时间戳类型
        if (mapping.TryGetValue("timestamp", out var tsGroup) && !ignored.Contains(tsGroup) && m.Groups[tsGroup].Success)
        {
            var digits = m.Groups[tsGroup].Value;
            if (pattern.TimestampLength is int len && digits.Length > len)
                digits = digits[..len];
            return TimestampToDate(digits, pattern.TimestampLength);
        }

        // 年月日时分秒类型
        int Group(string key, int @default = 0)
        {
            if (!mapping.TryGetValue(key, out var idx)) return @default;
            if (ignored.Contains(idx)) return @default;
            var g = m.Groups[idx];
            return g.Success && int.TryParse(g.Value, out var v) ? v : @default;
        }

        var year = Group("year", -1);
        if (year < 1) return null;
        // 合理年份外的结果视为未命中，让提取循环继续尝试下一模式
        // （用户 patterns.json 里的旧正则不会自动获得数字边界，紧凑日期可能从时间戳里抠出 16xx 年）
        if (year is < 1970 or > 2100) return null;
        var month = Group("month", 1);
        var day = Group("day", 1);
        var hour = Group("hour");
        var minute = Group("minute");
        var second = Group("second");

        try
        {
            var dt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            return new DateTimeOffset(dt, LocalTz.GetUtcOffset(dt));
        }
        catch
        {
            return null; // 非法日期（如 2024-02-31）
        }
    }

    private static DateTimeOffset? TimestampToDate(string digits, int? length)
    {
        if (!long.TryParse(digits, out var ts)) return null;
        try
        {
            return length switch
            {
                13 => DateTimeOffset.FromUnixTimeMilliseconds(ts),
                16 => DateTimeOffset.FromUnixTimeMilliseconds(ts / 1000), // 微秒×10
                _ => DateTimeOffset.FromUnixTimeSeconds(ts)               // 10 位秒级
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
