using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaOrganizer.Core.Configuration;

/// <summary>一条文件名日期解析模式（patterns.json）。结构见 SRS §5.3。</summary>
public sealed class PatternDefinition
{
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "";

    /// <summary>捕获组名 → 组号。支持键：year/month/day/hour/minute/second/timestamp。</summary>
    public Dictionary<string, int> GroupMapping { get; set; } = new();

    public List<int> IgnoredGroups { get; set; } = new();

    /// <summary>时间戳位数：10（秒）/ 13（毫秒）/ 16（微秒×10，除以 1000）。</summary>
    public int? TimestampLength { get; set; }

    public bool Enabled { get; set; } = true;
    public double Weight { get; set; } = 1.0;
    public bool Builtin { get; set; }
}

/// <summary>patterns.json 的读写。</summary>
public static class PatternsStore
{
    public static List<PatternDefinition> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("patterns", out var arr))
                {
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<PatternDefinition>>(
                        arr.GetRawText(), ConfigManager.JsonOptions);
                    if (list is not null && list.Count > 0) return list;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"patterns.json 读取失败，使用内置模式: {ex.Message}");
        }
        return GetBuiltinPatterns();
    }

    public static void Save(string path, IReadOnlyList<PatternDefinition> patterns)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var payload = new { version = 1, patterns };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(payload, ConfigManager.JsonOptions));
    }

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
            Name = "毫秒级时间戳", Pattern = @"(^|[^0-9])(\d{13})([^0-9]|$)",
            GroupMapping = new() { ["timestamp"] = 2 }, TimestampLength = 13,
            Weight = 0.7, Builtin = true
        }
    ];
}
