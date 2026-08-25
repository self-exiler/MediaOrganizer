namespace MediaOrganizer.Core.Extraction;

/// <summary>日期范围校验：1970-01-01 ~ 2100-12-31；不早于（当前年份 − maxYearsPast）；不晚于（当前 + futureDateBufferDays）。</summary>
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
        var latest = _now.AddDays(_futureDateBufferDays);
        return date >= earliest && date <= latest;
    }
}
