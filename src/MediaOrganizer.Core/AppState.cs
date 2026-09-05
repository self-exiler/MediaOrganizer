using MediaOrganizer.Core.Configuration;

namespace MediaOrganizer.Core;

/// <summary>
/// 应用状态单所有者：持有 config + patterns + 各自路径，唯一保存入口与变更通知。
/// ViewModel 只读写这里的 Config/Patterns，保存统一走 SaveAll/SaveConfig/SavePatterns，
/// 消灭"多个 VM 各自存、事件名实不符"的配置旅行（架构候选 3）。
/// </summary>
public sealed class AppState
{
    private readonly string _configPath;
    private readonly string _patternsPath;

    public AppState(AppConfig config, List<PatternDefinition> patterns, string configPath, string patternsPath)
    {
        Config = config;
        Patterns = patterns;
        _configPath = configPath;
        _patternsPath = patternsPath;
    }

    public AppConfig Config { get; }
    public List<PatternDefinition> Patterns { get; }

    /// <summary>任意配置/模式变更后触发（保存或即时生效均通知，工作台据此重建提取链）。</summary>
    public event Action? Changed;

    /// <summary>加载时配置/模式文件的解析错误；正常加载为 null。UI 须据此提示用户，不得静默回退出厂设置。</summary>
    public string? LoadError { get; private init; }

    /// <summary>损坏配置文件被隔离后的副本路径（保留供人工恢复）；未发生隔离时为 null。</summary>
    public string? QuarantinedConfigPath { get; private init; }

    /// <summary>损坏 patterns.json 被隔离后的副本路径；未发生隔离时为 null（P2-4：与配置同规则，免被 SavePatterns 覆盖而永久丢失）。</summary>
    public string? QuarantinedPatternsPath { get; private init; }

    public static AppState Load(string configPath, string patternsPath)
    {
        string? configError = null;
        var config = JsonFileStore.Load<AppConfig>(configPath, e => configError = e);

        string? quarantined = null;
        if (config is null && configError is not null && File.Exists(configPath))
            quarantined = JsonFileStore.QuarantineCorrupt(configPath);

        string? patternsError = null;
        var patterns = PatternsStore.Load(patternsPath, e => patternsError = e);

        // 与 config 对齐：解析失败且文件确实存在 → 隔离，避免随后某次 SavePatterns 用内置模式覆盖损坏文件、用户自定义模式永久丢失。
        string? quarantinedPatterns = null;
        if (patternsError is not null && File.Exists(patternsPath))
            quarantinedPatterns = JsonFileStore.QuarantineCorrupt(patternsPath);

        var error = (configError, patternsError) switch
        {
            (null, null) => null,
            (not null, null) => configError,
            (null, not null) => patternsError,
            _ => $"{configError}；{patternsError}"
        };

        return new AppState(config ?? new AppConfig(), patterns, configPath, patternsPath)
        {
            LoadError = error,
            QuarantinedConfigPath = quarantined,
            QuarantinedPatternsPath = quarantinedPatterns
        };
    }

    /// <summary>
    /// 保存配置。默认触发 Changed（配置变更 → 工作台重建提取链并失效旧计划）；
    /// 仅持久化执行偏好等不影响提取链的字段时传 notifyChanged: false，避免误清计划。
    /// </summary>
    public void SaveConfig(bool notifyChanged = true)
    {
        JsonFileStore.Save(_configPath, Config);
        if (notifyChanged) Changed?.Invoke();
    }

    public void SavePatterns()
    {
        PatternsStore.Save(_patternsPath, Patterns);
        Changed?.Invoke();
    }

    public void SaveAll()
    {
        SaveConfig();
        SavePatterns();
    }

    /// <summary>仅通知变更（内存即时生效但尚未落盘时调用），工作台据此提示重新分析。</summary>
    public void NotifyChanged() => Changed?.Invoke();
}
