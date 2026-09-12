using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Planning;

namespace MediaOrganizer.Core.Tests;

public class ArchivePlannerTests
{
    private static ParsedFile Pf(string name, int year, int month = 1, int day = 1) =>
        new(new MediaFile(Path.Combine(@"C:\src", name), 10, "jpg"),
            new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero), "FileName");

    private static AnalysisResult Result(params ParsedFile[] files) =>
        new(@"C:\src", new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero), files, []);

    [Fact]
    public void 按日分级()
    {
        var plan = new ArchivePlanner(ClassificationLevel.Day).Plan(Result(Pf("a.jpg", 2024, 1, 15)), @"D:\out");
        Assert.Equal("2024/01/15/a.jpg", plan.Files[0].RelativeTarget);
    }

    [Fact]
    public void 按月分级()
    {
        var plan = new ArchivePlanner(ClassificationLevel.Month).Plan(Result(Pf("a.jpg", 2024, 1, 15)), @"D:\out");
        Assert.Equal("2024/01/a.jpg", plan.Files[0].RelativeTarget);
    }

    [Fact]
    public void 按年分级()
    {
        var plan = new ArchivePlanner(ClassificationLevel.Year).Plan(Result(Pf("a.jpg", 2024, 1, 15)), @"D:\out");
        Assert.Equal("2024/a.jpg", plan.Files[0].RelativeTarget);
    }

    [Fact]
    public void 未来日期进入FutureDate()
    {
        // 2026-08-08 + 缓冲 1 天 = 08-09；08-11 超出 → 归入 FutureDate/
        var file = new ParsedFile(
            new MediaFile(@"C:\src\f.jpg", 10, "jpg"),
            new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero), "Exif");
        var now = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);
        var plan = new ArchivePlanner(ClassificationLevel.Day, futureDateBufferDays: 1, now).Plan(Result(file), @"D:\out");
        Assert.Equal("FutureDate/2026/08/11/f.jpg", plan.Files[0].RelativeTarget);
    }

    [Fact]
    public void 缓冲内未来日期不进FutureDate()
    {
        // 08-09 ≤ 08-08 + 1 天缓冲 → 正常归档，不进 FutureDate/
        var file = new ParsedFile(
            new MediaFile(@"C:\src\g.jpg", 10, "jpg"),
            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero), "Exif");
        var now = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);
        var plan = new ArchivePlanner(ClassificationLevel.Day, futureDateBufferDays: 1, now).Plan(Result(file), @"D:\out");
        Assert.Equal("2026/08/09/g.jpg", plan.Files[0].RelativeTarget);
    }
}
