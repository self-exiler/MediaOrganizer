using MediaOrganizer.Core;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Patterns;

namespace MediaOrganizer.Core.Tests;

public class OrganizeSessionTests
{
    private static AnalysisResult Result() =>
        new(@"C:\src", new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero),
        [
            new ParsedFile(new MediaFile(@"C:\src\a.jpg", 10, "jpg"),
                new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero), "FileName")
        ], []);

    [Fact]
    public void 新结果就位后自动生成计划()
    {
        var s = new OrganizeSession(new AppConfig());
        Assert.Null(s.CurrentPlan);
        s.SetResult(Result(), @"D:\out");
        Assert.NotNull(s.CurrentPlan);
        Assert.Equal("2024/01/15/a.jpg", s.CurrentPlan!.Files[0].RelativeTarget);
    }

    [Fact]
    public void 修改级别立即重规划()
    {
        var s = new OrganizeSession(new AppConfig(), ClassificationLevel.Day);
        s.SetResult(Result(), @"D:\out");
        Assert.Equal("2024/01/15/a.jpg", s.CurrentPlan!.Files[0].RelativeTarget);

        // 改为按月 → 计划立即重建（修复"改分级后执行旧计划"bug 的回归测试）
        s.Level = ClassificationLevel.Month;
        Assert.Equal("2024/01/a.jpg", s.CurrentPlan!.Files[0].RelativeTarget);
    }

    [Fact]
    public void 失效结果后不可执行()
    {
        var s = new OrganizeSession(new AppConfig());
        s.SetResult(Result(), @"D:\out");
        s.InvalidateResult();
        Assert.Null(s.CurrentPlan);
        Assert.False(s.HasResult);
    }
}

public class AppStateNotifyTests
{
    private static (AppState State, string ConfigPath, string PatternsPath) NewState()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mo-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (new AppState(new AppConfig(), PatternsStore.GetBuiltinPatterns(),
            Path.Combine(dir, "config.json"), Path.Combine(dir, "patterns.json")),
            Path.Combine(dir, "config.json"), Path.Combine(dir, "patterns.json"));
    }

    [Fact]
    public void SaveConfig默认触发Changed()
    {
        var (state, _, _) = NewState();
        var fired = 0;
        state.Changed += () => fired++;
        state.SaveConfig();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SaveConfig静默模式不触发Changed()
    {
        var (state, _, _) = NewState();
        var fired = 0;
        state.Changed += () => fired++;
        state.SaveConfig(notifyChanged: false);
        Assert.Equal(0, fired);
    }
}

public class PatternInferrerTests
{
    [Fact]
    public void 命名组优先()
    {
        var p = PatternInferrer.Infer(@"(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})", ["2024-01-15.jpg"]);
        Assert.NotNull(p);
        Assert.Equal(1, p!.GroupMapping["year"]);
        Assert.Equal(3, p.GroupMapping["day"]);
    }

    [Fact]
    public void 位置推断三组为年月日()
    {
        var p = PatternInferrer.Infer(@"IMG_(\d{4})(\d{2})(\d{2})", ["IMG_20240115.jpg"]);
        Assert.NotNull(p);
        Assert.Equal(1, p!.GroupMapping["year"]);
        Assert.Equal(3, p.GroupMapping["day"]);
    }

    [Fact]
    public void 位置推断时间戳()
    {
        var p = PatternInferrer.Infer(@"mm_export(\d{13})", ["mm_export1718012345678.jpg"]);
        Assert.NotNull(p);
        Assert.Equal(1, p!.GroupMapping["timestamp"]);
        Assert.Equal(13, p.TimestampLength);
    }

    [Fact]
    public void 圈选标记生成正则()
    {
        var cells = new List<CharCell>
        {
            new('m', MarkRole.None), new('m', MarkRole.None), new('_', MarkRole.None),
            new('2', MarkRole.Year), new('0', MarkRole.Year), new('2', MarkRole.Year), new('4', MarkRole.Year),
            new('0', MarkRole.Month), new('1', MarkRole.Month),
            new('1', MarkRole.Day), new('5', MarkRole.Day)
        };
        var p = PatternInferrer.GenerateFromMarks("mm_20240115", cells);
        Assert.NotNull(p);
        Assert.Equal(@"mm_(\d{4})(\d{2})(\d{2})", p!.Pattern);
        Assert.Equal(1, p.GroupMapping["year"]);
        Assert.Equal(3, p.GroupMapping["day"]);
    }

    [Fact]
    public void 多变体生成覆盖差异位()
    {
        var regex = PatternInferrer.GenerateVariantRegex(
            ["mm_export1718012345678.jpg", "mm_export1718012398745.jpg", "mm_export1718012456123.jpg"]);
        // 公共前缀 mm_export + 13 位数字 + .jpg
        Assert.Contains(@"mm_export", regex);
        Assert.Contains(@"\.jpg", regex);
        foreach (var s in new[] { "mm_export1718012345678.jpg", "mm_export1718012456123.jpg" })
            Assert.Matches(regex, s);
    }

    [Fact]
    public void 字符着色分类()
    {
        var cells = PatternInferrer.ToCharCells("a1_");
        Assert.Equal(new CharCell('a', MarkRole.None), cells[0]);
        Assert.Equal(new CharCell('1', MarkRole.None), cells[1]);
        Assert.Equal(new CharCell('_', MarkRole.None), cells[2]);
    }
}

public class AnalysisResultStoreRoundTripTests
{
    [Fact]
    public void 保存后加载还原()
    {
        var result = new AnalysisResult(@"C:\src", new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero),
        [
            new ParsedFile(new MediaFile(@"C:\src\a.jpg", 10, "jpg"),
                new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero), "FileName")
        ],
        [
            new UnparsedFile(new MediaFile(@"C:\src\b.tif", 5, "tif"), "NoValidDate")
        ]);
        var path = Path.Combine(Path.GetTempPath(), "mo-result-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AnalysisResultStore.Save(path, result);
            var loaded = AnalysisResultStore.Load(path);
            Assert.NotNull(loaded);
            Assert.Equal(1, loaded!.Parsed.Count);
            Assert.Equal("a.jpg", loaded.Parsed[0].File.FileName);
            Assert.Equal("FileName", loaded.Parsed[0].Source);
            Assert.Equal("b.tif", loaded.Unparsed[0].File.FileName);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 缺失文件返回空()
    {
        var path = Path.Combine(Path.GetTempPath(), "mo-result-none-" + Guid.NewGuid().ToString("N") + ".json");
        Assert.Null(AnalysisResultStore.Load(path));
    }
}
