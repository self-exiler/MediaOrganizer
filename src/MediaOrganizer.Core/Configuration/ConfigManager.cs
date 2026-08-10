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
        => JsonFileStore.Load<AppConfig>(path) ?? new AppConfig();

    public static void Save(string path, AppConfig config)
        => JsonFileStore.Save(path, config);
}
