using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Core.Extraction;

/// <summary>一种日期来源策略。</summary>
public interface IDateExtractor
{
    string Name { get; }
    bool Enabled { get; }
    double Weight { get; }

    /// <summary>尝试提取拍摄日期；失败返回 null（不抛异常）。</summary>
    DateTimeOffset? Extract(MediaFile file);
}

/// <summary>加权链成功提取的结果。</summary>
public sealed record ExtractResult(DateTimeOffset Date, string Source);
