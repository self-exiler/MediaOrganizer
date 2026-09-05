using System.Text.RegularExpressions;
using MediaOrganizer.Core.Patterns;

namespace MediaOrganizer.Core.Tests;

/// <summary>
/// FR-7.4 多变体智能生成 + FR-7.3 圈选生成（P0-3）。
/// 防回归重点：修复前逐位输出 \d{1}、不产生捕获组，导致 Infer 的 GroupMapping 恒空、
/// TryExtract 永不命中，SavePattern 无法通过；且同数字前缀（2023/2024 前三位"202"）会把日期字段拆碎。
/// </summary>
public class PatternInferrerTests
{
    [Fact]
    public void 空样本返回空串()
    {
        Assert.Equal("", PatternInferrer.GenerateVariantRegex([]));
    }

    [Fact]
    public void 单样本整体转义为字面量()
    {
        var r = PatternInferrer.GenerateVariantRegex(["IMG_20240115.jpg"]);
        // 无反斜杠转义的普通模板 → 引用等价于原文
        Assert.Equal("IMG_20240115\\.jpg", r);
        Assert.True(Regex.IsMatch("IMG_20240115.jpg", r));
    }

    /// <summary>同前缀日期字段（2023/2024 前三位"202"全同）不得被拆碎，须整块产出 4 位 year 组。</summary>
    [Fact]
    public void 同数字前缀日期不拆碎()
    {
        var samples = new[] { "IMG_20230115.jpg", "IMG_20240115.jpg" };
        var r = PatternInferrer.GenerateVariantRegex(samples);

        foreach (var s in samples) Assert.True(Regex.IsMatch(s, r), $"正则应命中 {s}：{r}");

        // 两个样本各多一个捕获组（同前缀年份是 4 位整数块中的唯一差异），可被语义推断为 year
        var p = PatternInferrer.Infer(r, samples)!;
        Assert.True(p.GroupMapping.Count > 0, $"生成的组映射不应为空：{r} → 组 {p.GroupMapping.Count}");
        Assert.Equal(2023, ExtractYear(r, samples[0]));
        Assert.Equal(2024, ExtractYear(r, samples[1]));
    }

    [Fact]
    public void 分隔符日期整块年月日()
    {
        var samples = new[] { "IMG_2023-01-15.jpg", "IMG_2024-02-16.jpg" };
        var r = PatternInferrer.GenerateVariantRegex(samples);

        foreach (var s in samples) Assert.True(Regex.IsMatch(s, r), $"正则应命中 {s}：{r}");

        var p = PatternInferrer.Infer(r, samples)!;
        foreach (var s in samples)
        {
            var m = Regex.Match(s, r);
            Assert.Equal(4, m.Groups["year"].Value.Length);
            Assert.Equal(2, m.Groups["month"].Value.Length);
            Assert.Equal(2, m.Groups["day"].Value.Length);
        }
    }

    [Fact]
    public void 紧凑时间戳14位展开为年月日时分秒()
    {
        var samples = new[] { "VID_20240115143022.mp4", "VID_20250116153033.mp4" };
        var r = PatternInferrer.GenerateVariantRegex(samples);

        foreach (var s in samples) Assert.True(Regex.IsMatch(s, r), $"正则应命中 {s}：{r}");

        var p = PatternInferrer.Infer(r, samples)!;
        var m = Regex.Match(samples[0], r);
        Assert.Equal("20240115", m.Groups["year"].Value + m.Groups["month"].Value + m.Groups["day"].Value);
        Assert.Equal("143022", m.Groups["hour"].Value + m.Groups["minute"].Value + m.Groups["second"].Value);
    }

    [Fact]
    public void 推断生成模式可实际解析出日期()
    {
        var samples = new[] { "hack_20240506123000_aa.png", "hack_20240507120000_bb.png" };
        var r = PatternInferrer.GenerateVariantRegex(samples);

        // 生成出的正则 + Infer 得到的组映射应能被解析链路回用，产出合法日期
        var p = PatternInferrer.Infer(r, samples)!;
        Assert.NotNull(p);
        var m = Regex.Match(samples[0], r);
        Assert.True(m.Success);
        Assert.Equal("2024", m.Groups["year"].Value);
    }

    /// <summary>便利：从命名 year 组读年份。</summary>
    private static int ExtractYear(string regex, string sample)
        => int.Parse(Regex.Match(sample, regex).Groups["year"].Value);
}