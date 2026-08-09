using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Patterns;

namespace MediaOrganizer.Core.Tests;

public class PatternEngineTests
{
    private static readonly List<PatternDefinition> Builtins = PatternsStore.GetBuiltinPatterns();

    [Fact]
    public void 微信导出13位时间戳()
    {
        var p = Builtins.First(x => x.Name == "微信导出");
        var d = PatternEngine.TryExtract("mm_export1718012345678.jpg", p);
        Assert.NotNull(d);
        Assert.Equal(2024, d!.Value.Year);
        Assert.Equal(6, d.Value.Month);
    }

    [Fact]
    public void 通用日期时间()
    {
        var d = PatternEngine.TryExtract("2024-01-15 14-30-00_photo.jpg", Builtins);
        Assert.NotNull(d);
        Assert.Equal(new DateTime(2024, 1, 15, 14, 30, 0), d.Value.DateTime);
    }

    [Fact]
    public void 紧凑日期()
    {
        var d = PatternEngine.TryExtract("IMG_20240115_123456.jpg", Builtins);
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
        Assert.Null(PatternEngine.TryExtract("scan001.tif", Builtins));
    }

    [Fact]
    public void 快速预扫描判定()
    {
        Assert.True(PatternEngine.ContainsLikelyDate("IMG_20240115.jpg", Builtins));
        Assert.True(PatternEngine.ContainsLikelyDate("mm_export1718012345678.jpg", Builtins)); // 时间戳模式
        Assert.False(PatternEngine.ContainsLikelyDate("scan001.tif", Builtins));
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
