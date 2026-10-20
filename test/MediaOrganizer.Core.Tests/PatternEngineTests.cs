using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Patterns;

namespace MediaOrganizer.Core.Tests;

public class PatternEngineTests
{
    private static readonly List<PatternDefinition> Builtins = PatternsStore.GetBuiltinPatterns();

    // 运行时 FileNameExtractor 的循环语义（列表版重载已作为「仅测试用」删除，C6）
    private static DateTimeOffset? ExtractAll(string name)
    {
        foreach (var p in Builtins.Where(p => p.Enabled).OrderByDescending(p => p.Weight))
            if (PatternEngine.TryExtract(name, p) is DateTimeOffset d)
                return d;
        return null;
    }

    private static bool LikelyDate(string name)
        => PatternEngine.ContainsLikelyDate(name, Builtins.Any(p => p.Enabled && p.TimestampLength is not null));

    [Fact]
    public void 微信导出13位时间戳()
    {
        var p = Builtins.First(x => x.Name == "微信导出");
        var d = PatternEngine.TryExtract("mm_export1718012345678.jpg", p);
        Assert.NotNull(d);
        Assert.Equal(2024, d!.Value.Year);
        Assert.Equal(6, d.Value.Month);
    }

    /// <summary>
    /// 防回归：小红书与 OPPO 两条内置模式曾因正则只有 4 个捕获组、
    /// GroupMapping 却引用 group 5/6 而 100% 失效（且 6 位 HHmmss 被当 hour 抛异常被吞）。
    /// 本测试锁住整类缺陷，而非只锁这两条。
    /// </summary>
    [Fact]
    public void 所有内置模式的GroupMapping不越界()
    {
        foreach (var p in Builtins)
        {
            var groupCount = new System.Text.RegularExpressions.Regex(p.Pattern).GetGroupNames().Length - 1; // 去掉 group 0
            var bad = p.GroupMapping
                .Where(kv => kv.Value > groupCount)
                .Select(kv => $"{kv.Key}={kv.Value}")
                .ToArray();

            Assert.True(bad.Length == 0,
                $"模式「{p.Name}」的 GroupMapping 引用了不存在的捕获组（正则共 {groupCount} 个捕获组）：{string.Join(", ", bad)}");
        }
    }

    [Fact]
    public void 小红书日期时间()
    {
        var p = Builtins.First(x => x.Name == "小红书");
        var d = PatternEngine.TryExtract("20240115-143022.jpg", p);
        Assert.NotNull(d);
        Assert.Equal(new DateTime(2024, 1, 15, 14, 30, 22), d!.Value.DateTime);
    }

    [Fact]
    public void OPPO相机日期时间()
    {
        var p = Builtins.First(x => x.Name == "OPPO 相机");
        var d = PatternEngine.TryExtract("IMG_20240115_143022.jpg", p);
        Assert.NotNull(d);
        Assert.Equal(new DateTime(2024, 1, 15, 14, 30, 22), d!.Value.DateTime);
    }

    [Fact]
    public void 通用日期时间()
    {
        var d = ExtractAll("2024-01-15 14-30-00_photo.jpg");
        Assert.NotNull(d);
        Assert.Equal(new DateTime(2024, 1, 15, 14, 30, 0), d.Value.DateTime);
    }

    [Fact]
    public void 紧凑日期()
    {
        var d = ExtractAll("IMG_20240115_123456.jpg");
        Assert.NotNull(d);
        Assert.Equal(2024, d!.Value.Year);
        Assert.Equal(1, d.Value.Month);
        Assert.Equal(15, d.Value.Day);
    }

    [Fact]
    public void 非法日期返回null()
    {
        var p = new PatternDefinition
        {
            Name = "非法日期", Pattern = @"(\d{4})(\d{2})(\d{2})",
            GroupMapping = new() { ["year"] = 1, ["month"] = 2, ["day"] = 3 }
        };
        Assert.Null(PatternEngine.TryExtract("20240231_scan.jpg", p)); // 2月31日
    }

    [Fact]
    public void 无日期文件名返回null()
    {
        Assert.Null(ExtractAll("scan001.tif"));
    }

    [Fact]
    public void 快速预扫描判定()
    {
        Assert.True(LikelyDate("IMG_20240115.jpg"));
        Assert.True(LikelyDate("mm_export1718012345678.jpg")); // 时间戳模式
        Assert.False(LikelyDate("scan001.tif"));
    }

    [Fact]
    public void 禁用模式不参与()
    {
        var p = new PatternDefinition
        {
            Name = "已禁用", Pattern = @"(\d{4})(\d{2})(\d{2})",
            GroupMapping = new() { ["year"] = 1, ["month"] = 2, ["day"] = 3 }, Enabled = false
        };
        Assert.Null(PatternEngine.TryExtract("20240115.jpg", p));
    }
}
