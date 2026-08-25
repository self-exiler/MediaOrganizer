using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaOrganizer.Core.Configuration;

/// <summary>JSON 文件原子读写帮助（建目录 + 序列化 + 落盘），供配置/模式/分析结果共用。</summary>
public static class JsonFileStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save<T>(string path, T payload)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"JSON 读取失败 {path}: {ex.Message}");
            return null;
        }
    }
}
