namespace MediaOrganizer.Core.Extraction;

/// <summary>日期范围校验：1970-01-01 ~ 2100-12-31；不早于（当前年份 − maxYearsPast）；不晚于（当前日期 23:59:59 + futureDateBufferDays）。</summary>
public sealed class DateRangeValidator(int maxYearsPast = 30, int futureDateBufferDays = 0, DateTimeOffset? now = null)
{
    private readonly int _maxYearsPast = maxYearsPast >= 0
        ? maxYearsPast
        : throw new ArgumentOutOfRangeException(nameof(maxYearsPast), "must be non-negative");
    private readonly int _futureDateBufferDays = futureDateBufferDays >= 0
        ? futureDateBufferDays
        : throw new ArgumentOutOfRangeException(nameof(futureDateBufferDays), "must be non-negative");
    private readonly DateTimeOffset _now = now ?? DateTimeOffset.Now;

    public bool IsValid(DateTimeOffset date)
    {
        if (date.Year < 1970 || date.Year > 2100) return false;
        var earliest = new DateTimeOffset(_now.Year - _maxYearsPast, 1, 1, 0, 0, 0, _now.Offset);
        // FR-2.4：上界按"日"而非"时刻"判定，取允许日的 23:59:59.9999999。
        // 若用当前时刻，早上分析当天下午拍摄的照片会被误判为未来日期并落入失败清单。
        var upperDay = _now.Date.AddDays(_futureDateBufferDays);
        var latest = new DateTimeOffset(upperDay.AddDays(1).AddTicks(-1), _now.Offset);
        return date >= earliest && date <= latest;
    }
}
