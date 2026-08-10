using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MediaOrganizer.Core.Configuration;

namespace MediaOrganizer.Core.Patterns;

/// <summary>文件名模式引擎：正则匹配 + group_mapping + 时间戳解析。</summary>
public static class PatternEngine
{
    private static readonly TimeZoneInfo LocalTz = TimeZoneInfo.Local;

    // 预编译热路径正则（避免每文件重新编译）
    private static readonly Regex TimestampRegex = new(@"\d{10,}", RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"19[7-9]\d|20\d\d|21\d\d", RegexOptions.Compiled);

    // 正则编译缓存：pattern string → compiled Regex（线程安全）
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new();

    private static Regex? GetCachedRegex(string pattern)
        => RegexCache.GetOrAdd(pattern, p =>
        {
            try { return new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant); }
            catch { return null; }
        });

    /// <summary>快速预扫描：文件名中是否可能存在日期（年份或 ≥10 位连续数字时间戳），避免对每个文件跑全部正则。</summary>
    public static bool ContainsLikelyDate(string fileName, IReadOnlyList<PatternDefinition> patterns)
    {
        // 存在时间戳模式时：文件名需含 ≥10 位连续数字才算疑似
        if (patterns.Any(p => p.Enabled && p.TimestampLength is not null)
            && TimestampRegex.IsMatch(fileName))
            return true;
        // 4 位年份（1970-2100），允许嵌入更长的数字串（如紧凑日期 20240115）
        foreach (Match m in YearRegex.Matches(fileName))
        {
            if (int.TryParse(m.Value, out var y) && y >= 1970 && y <= 2100) return true;
        }
        return false;
    }

    /// <summary>按全部启用的模式依次尝试（按权重降序），返回第一个解析成功且构造合法的日期。</summary>
    public static DateTimeOffset? TryExtract(string fileName, IReadOnlyList<PatternDefinition> patterns)
    {
        foreach (var pattern in patterns.Where(p => p.Enabled).OrderByDescending(p => p.Weight))
        {
            if (TryExtract(fileName, pattern) is DateTimeOffset d) return d;
        }
        return null;
    }

    public static DateTimeOffset? TryExtract(string fileName, PatternDefinition pattern)
    {
        if (!pattern.Enabled || string.IsNullOrEmpty(pattern.Pattern)) return null;

        var regex = GetCachedRegex(pattern.Pattern);
        if (regex is null) return null;

        var m = regex.Match(fileName);
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
