using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaOrganizer.Core.Configuration;

public static class ConfigManager
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions);
                if (cfg is not null) return cfg;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"config.json 读取失败，使用默认配置: {ex.Message}");
        }
        return new AppConfig();
    }

    public static void Save(string path, AppConfig config)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }
}
