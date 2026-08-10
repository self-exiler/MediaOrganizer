namespace MediaOrganizer.Core.Configuration;

/// <summary>一条文件名日期解析模式（patterns.json）。结构见 SRS §5.3。</summary>
public sealed class PatternDefinition
{
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "";

    /// <summary>捕获组名 → 组号。支持键：year/month/day/hour/minute/second/timestamp。</summary>
    public Dictionary<string, int> GroupMapping { get; set; } = new();

    /// <summary>明确忽略的捕获组号（如前后缀 (.*) 组），解析与推断时跳过（FR-3.1）。</summary>
    public List<int> IgnoredGroups { get; set; } = [];

    /// <summary>时间戳位数：10（秒）/ 13（毫秒）/ 16（微秒×10，除以 1000）。</summary>
    public int? TimestampLength { get; set; }

    public bool Enabled { get; set; } = true;
    public double Weight { get; set; } = 1.0;
    public bool Builtin { get; set; }
}

/// <summary>patterns.json 顶层结构。</summary>
public sealed class PatternsFile
{
    public int Version { get; set; } = 1;
    public List<PatternDefinition> Patterns { get; set; } = [];
}

/// <summary>patterns.json 的读写。</summary>
public static class PatternsStore
{
    public static List<PatternDefinition> Load(string path)
        => JsonFileStore.Load<PatternsFile>(path) is { Patterns.Count: > 0 } file
            ? file.Patterns
            : GetBuiltinPatterns();

    public static void Save(string path, IReadOnlyList<PatternDefinition> patterns)
        => JsonFileStore.Save(path, new PatternsFile { Patterns = patterns.ToList() });

    /// <summary>内置模式：覆盖微信/小红书/OPPO/通用日期/紧凑日期/时间戳等常见场景。</summary>
    public static List<PatternDefinition> GetBuiltinPatterns() =>
    [
        new()
        {
            Name = "微信导出", Pattern = @"mm_export(\d{13})",
            GroupMapping = new() { ["timestamp"] = 1 }, TimestampLength = 13, Weight = 1.0, Builtin = true
        },
        new()
        {
            Name = "小红书", Pattern = @"(\d{4})(\d{2})(\d{2})[-_](\d{6})",
            GroupMapping = new() { ["year"] = 1, ["month"] = 2, ["day"] = 3, ["hour"] = 4, ["minute"] = 5, ["second"] = 6 },
            Weight = 0.9, Builtin = true
        },
        new()
        {
            Name = "通用日期时间", Pattern = @"(\d{4})[-_.](\d{1,2})[-_.](\d{1,2})[T _](\d{1,2})[-_.:](\d{2})[-_.:](\d{2})",
            GroupMapping = new() { ["year"] = 1, ["month"] = 2, ["day"] = 3, ["hour"] = 4, ["minute"] = 5, ["second"] = 6 },
            Weight = 1.0, Builtin = true
        },
        new()
        {
            Name = "通用日期", Pattern = @"(\d{4})[-_.](\d{1,2})[-_.](\d{1,2})",
            GroupMapping = new() { ["year"] = 1, ["month"] = 2, ["day"] = 3 },
            Weight = 0.9, Builtin = true
        },
        new()
        {
            Name = "紧凑日期时间", Pattern = @"(\d{4})(\d{2})(\d{2})[ _]?(\d{2})(\d{2})(\d{2})",
            GroupMapping = new() { ["year"] = 1, ["month"] = 2, ["day"] = 3, ["hour"] = 4, ["minute"] = 5, ["second"] = 6 },
            Weight = 0.9, Builtin = true
        },
        new()
        {
            Name = "紧凑日期", Pattern = @"(\d{4})(\d{2})(\d{2})",
            GroupMapping = new() { ["year"] = 1, ["month"] = 2, ["day"] = 3 },
            Weight = 0.8, Builtin = true
        },
        new()
        {
            Name = "秒级时间戳", Pattern = @"(^|[^0-9])(\d{10})([^0-9]|$)",
            GroupMapping = new() { ["timestamp"] = 2 }, TimestampLength = 10,
            Weight = 0.7, Builtin = true
        },
        new()
        {
            Name = "OPPO 相机", Pattern = @"IMG_(\d{4})(\d{2})(\d{2})_(\d{6})",
            GroupMapping = new() { ["year"] = 1, ["month"] = 2, ["day"] = 3, ["hour"] = 4, ["minute"] = 5, ["second"] = 6 },
            Weight = 0.8, Builtin = true
        },
        new()
        {
            Name = "毫秒级时间戳", Pattern = @"(^|[^0-9])(\d{13})([^0-9]|$)",
            GroupMapping = new() { ["timestamp"] = 2 }, TimestampLength = 13,
            Weight = 0.7, Builtin = true
        },
        new()
        {
            Name = "微秒级时间戳", Pattern = @"(^|[^0-9])(\d{16})([^0-9]|$)",
            GroupMapping = new() { ["timestamp"] = 2 }, TimestampLength = 16,
            Weight = 0.6, Builtin = true
        }
    ];
}
