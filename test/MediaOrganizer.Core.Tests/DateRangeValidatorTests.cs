using MediaOrganizer.Core.Extraction;

namespace MediaOrganizer.Core.Tests;

public class DateRangeValidatorTests
{
    private static DateTimeOffset Dt(int y, int m = 1, int d = 1) =>
        new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 边界内日期有效()
    {
        var v = new DateRangeValidator(maxYearsPast: 30, futureDateBufferDays: 0, now: Dt(2026, 8, 8));
        Assert.True(v.IsValid(Dt(2024, 1, 15)));
        Assert.True(v.IsValid(Dt(1996, 1, 1))); // 最早允许：2026-30 = 1996-01-01
        Assert.True(v.IsValid(Dt(2026, 8, 8)));
    }

    [Fact]
    public void 超过过去年数无效()
    {
        var v = new DateRangeValidator(maxYearsPast: 30, futureDateBufferDays: 0, now: Dt(2026, 8, 8));
        Assert.False(v.IsValid(Dt(1995, 12, 31))); // 早于 1996-01-01
    }

    [Fact]
    public void 未来日期默认无效()
    {
        var v = new DateRangeValidator(maxYearsPast: 30, futureDateBufferDays: 0, now: Dt(2026, 8, 8));
        Assert.False(v.IsValid(Dt(2026, 8, 9)));
    }

    [Fact]
    public void 未来缓冲天数内有效()
    {
        var v = new DateRangeValidator(maxYearsPast: 30, futureDateBufferDays: 7, now: Dt(2026, 8, 8));
        Assert.True(v.IsValid(Dt(2026, 8, 14)));
        Assert.False(v.IsValid(Dt(2026, 8, 16)));
    }

    [Fact]
    public void 超出2100年无效()
    {
        var v = new DateRangeValidator(now: Dt(2026, 8, 8));
        Assert.False(v.IsValid(Dt(2101, 1, 1)));
    }
}
