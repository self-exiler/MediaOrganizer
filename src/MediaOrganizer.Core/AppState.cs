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

    /// <summary>任意配置/模式保存后触发（网络位置增删、UI 重读路径/目标/待处理目录等）。</summary>
    public event Action? Changed;

    /// <summary>影响提取链的变更（模式、提取器权重/开关、格式白名单、追溯年限等）后触发，
    /// 工作台仅订阅此事件重建链——网络位置等纯持久化变更不得作废已有分析结果（审计 11）。</summary>
    public event Action? ChainChanged;

    /// <summary>加载时配置/模式文件的解析错误；正常加载为 null。UI 须据此提示用户，不得静默回退出厂设置。</summary>
    public string? LoadError { get; private init; }

    /// <summary>损坏配置文件被隔离后的副本路径（保留供人工恢复）；未发生隔离时为 null。</summary>
    public string? QuarantinedConfigPath { get; private init; }

    /// <summary>损坏 patterns.json 被隔离后的副本路径；未发生隔离时为 null（P2-4：与配置同规则，免被 SavePatterns 覆盖而永久丢失）。</summary>
    public string? QuarantinedPatternsPath { get; private init; }

    public static AppState Load(string configPath, string patternsPath)
    {
        string? configError = null;
        var config = JsonFileStore.Load(configPath, AppJson.AppConfig, e => configError = e);

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
    /// 保存配置。notifyChanged: true = 影响提取链（Changed + ChainChanged，工作台重建链）；
    /// false = 完全静默仅落盘，需要通知 UI 刷新时由调用方显式 NotifyChanged / NotifyChainChanged。
    /// </summary>
    public void SaveConfig(bool notifyChanged = true)
    {
        JsonFileStore.Save(_configPath, Config, AppJson.AppConfig);
        if (notifyChanged) NotifyChainChanged();
    }

    public void SavePatterns()
    {
        PatternsStore.Save(_patternsPath, Patterns);
        NotifyChainChanged();
    }

    public void SaveAll()
    {
        SaveConfig();
        SavePatterns();
    }

    /// <summary>仅通知持久化类变更（内存即时生效但尚未落盘时调用），不重建提取链。</summary>
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>通知影响提取链的变更：工作台重建链，其余订阅方同步刷新。</summary>
    public void NotifyChainChanged()
    {
        Changed?.Invoke();
        ChainChanged?.Invoke();
    }
}
