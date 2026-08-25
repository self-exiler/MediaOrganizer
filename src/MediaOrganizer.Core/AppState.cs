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

    public static AppState Load(string configPath, string patternsPath)
        => new(JsonFileStore.Load<AppConfig>(configPath) ?? new AppConfig(), PatternsStore.Load(patternsPath), configPath, patternsPath);

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
