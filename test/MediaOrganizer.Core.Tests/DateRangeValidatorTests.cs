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

    /// <summary>
    /// 防回归：上界曾取"当前时刻"，导致早上 08:00 分析当天 14:00 拍摄的照片时被判为未来日期。
    /// FR-2.4 要求按"当前日期"判定，即当天任意时刻都必须有效。
    /// </summary>
    [Fact]
    public void 当天稍晚时刻的照片有效()
    {
        var v = new DateRangeValidator(maxYearsPast: 30, futureDateBufferDays: 0,
            now: new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero));

        Assert.True(v.IsValid(new DateTimeOffset(2026, 8, 8, 14, 30, 0, TimeSpan.Zero)), "当天下午拍摄的照片不得判为未来日期");
        Assert.True(v.IsValid(new DateTimeOffset(2026, 8, 8, 23, 59, 59, TimeSpan.Zero)), "当天最后一刻也应有效");
        Assert.False(v.IsValid(new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero)), "次日仍应无效");
    }

    [Fact]
    public void 未来缓冲按日判定()
    {
        var v = new DateRangeValidator(maxYearsPast: 30, futureDateBufferDays: 1,
            now: new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero));

        Assert.True(v.IsValid(new DateTimeOffset(2026, 8, 9, 23, 59, 0, TimeSpan.Zero)), "缓冲日全天有效");
        Assert.False(v.IsValid(new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void 超出2100年无效()
    {
        var v = new DateRangeValidator(now: Dt(2026, 8, 8));
        Assert.False(v.IsValid(Dt(2101, 1, 1)));
    }
}
