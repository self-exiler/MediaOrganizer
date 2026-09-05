using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Planning;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Shared.ViewModels;

/// <summary>
/// 整理工作台：选目录 → 分析 → 确认计划 → 执行归档（双端共享，ADR-0006 决策 4）。
/// 平台差异经注入抽象：目录选取走 IFolderPicker；分析器由组合根工厂构建（桌面本地扫描 + Magick EXIF，
/// Android SAF 扫描 + ExifInterface）；分析结果与报告落盘到应用专有目录（决策 6，不写源目录）。
/// </summary>
public partial class WorkbenchViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly AppLogger _logger;
    private readonly AppState _state;
    private readonly OrganizeSession _session;
    private readonly IFolderPicker _folderPicker;
    private readonly IJobHost? _jobHost;
    private CancellationTokenSource? _cts;
    // 从配置回读路径期间挂落盘，避免 ReloadPathsFromConfig 把回读值又写一遍（并触发多余的磁盘写）
    private bool _suspendPersist;

    public event Action<AnalysisResult>? AnalysisCompleted;

    /// <summary>最近一次分析生成的报告文本（透传 Session 缓存，P1-1 报告页复用）。</summary>
    public string? LastReportText => _session.LastReportText;

    public string[] OperationOptions { get; } = ["复制 copy（保留原文件）", "移动 move"];
    public string[] ExistActionOptions { get; } = ["跳过 skip", "覆盖 overwrite", "重命名 rename（_1、_2…）"];
    public string[] LevelOptions { get; } = ["按日 2024/01/15", "按月 2024/01", "按年 2024"];

    [ObservableProperty]
    private string _sourceDir;

    [ObservableProperty]
    private string _outputDir;

    [ObservableProperty]
    private string _pendingDir;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private int _totalFiles;

    [ObservableProperty]
    private int _successFiles;

    [ObservableProperty]
    private int _failedFiles;

    [ObservableProperty]
    private string _successRate = "--";

    /// <summary>按提取来源的统计摘要（如 "Exif 1,156 · FileName 68"），分析完成后更新。</summary>
    [ObservableProperty]
    private string _sourceSummary = "";

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _canExecute;

    [ObservableProperty]
    private string _planText = "请先完成分析";

    [ObservableProperty]
    private int _operationIndex;

    [ObservableProperty]
    private int _existActionIndex;

    [ObservableProperty]
    private int _levelIndex;

    [ObservableProperty]
    private bool _fixMtime;

    [ObservableProperty]
    private bool _isMoveAllowed = true;

    /// <summary>输出目标为本地目录（非网络位置）：移动端据此显示输出目录选取行（FR-A8.6/FR-A9.1）。</summary>
    [ObservableProperty]
    private bool _isLocalTarget = true;

    [ObservableProperty]
    private int _outputTargetIndex;

    /// <summary>输出目标选项：第 0 项为本地目录，其余为网络位置（FR-10）。</summary>
    public string[] OutputTargetOptions { get; private set; } = ["本地目录"];

    public WorkbenchViewModel(
        AppState state,
        AppLogger logger,
        IFolderPicker folderPicker,
        Func<Analyzer> analyzerFactory,
        string dataDir,
        IJobHost? jobHost = null)
    {
        _state = state;
        _config = state.Config;
        _logger = logger;
        _folderPicker = folderPicker;
        _jobHost = jobHost;

        _sourceDir = _config.Paths.SourceDir;
        _outputDir = _config.Paths.OutputDir;
        _pendingDir = _config.Paths.PendingDir;
        _fixMtime = _config.Execute.FixMtime;
        _operationIndex = _config.Execute.Operation == FileOperation.Move ? 1 : 0;
        _existActionIndex = _config.Execute.ExistAction.ToIndex();
        _levelIndex = _config.Execute.ClassificationLevel.ToIndex();

        _session = new OrganizeSession(
            _config,
            analyzerFactory,
            dataDir,
            _config.Execute.ClassificationLevel,
            _config.Execute.Operation,
            _config.Execute.ExistAction,
            _config.Execute.FixMtime);
        _session.PlanChanged += OnPlanChanged;
        _session.AnalysisCompleted += OnAnalysisCompleted;

        ReloadOutputTargets();
    }

    /// <summary>网络位置列表变化后刷新目标下拉并恢复选择（由主 VM 接线 State.Changed）。</summary>
    public void ReloadOutputTargets()
    {
        var selected = _config.Paths.OutputNetworkProfile;
        OutputTargetOptions = ["本地目录", .. _config.NetworkProfiles.Select(p => $"网络位置：{p.Name}")];
        OnPropertyChanged(nameof(OutputTargetOptions));
        var newIndex = string.IsNullOrEmpty(selected) ? 0 : Math.Max(0, 1 + _config.NetworkProfiles.FindIndex(p => p.Name == selected));
        IsLocalTarget = newIndex <= 0;
        if (newIndex != OutputTargetIndex)
            OutputTargetIndex = newIndex; // 触发 OnOutputTargetIndexChanged → 写回配置并刷新网络状态
    }

    partial void OnOutputTargetIndexChanged(int value)
    {
        IsLocalTarget = value <= 0;
        var profileName = value > 0 && value - 1 < _config.NetworkProfiles.Count
            ? _config.NetworkProfiles[value - 1].Name
            : "";
        _config.Paths.OutputNetworkProfile = profileName;
        _state.SaveConfig(notifyChanged: false); // 仅持久化目标，不失效计划
        UpdateNetworkState();
    }

    private void UpdateNetworkState()
    {
        var isNetwork = OutputTargetIndex > 0;
        IsMoveAllowed = !isNetwork;
        if (isNetwork && OperationIndex == 1) OperationIndex = 0; // 网络目标仅 copy
        if (_session.LastResult is { } result)
            _session.SetResult(result, DisplayTarget());
    }

    private string DisplayTarget()
    {
        var idx = OutputTargetIndex;
        if (idx > 0 && idx - 1 < _config.NetworkProfiles.Count)
            return $"网络位置：{_config.NetworkProfiles[idx - 1].Name}（仅复制）";
        return OutputDir;
    }

    private void OnPlanChanged(ArchivePlan plan)
    {
        var op = _session.Operation == FileOperation.Move ? "移动" : "复制";
        PlanText = $"将对 {plan.Files.Count} 个文件执行「{op}」到 {plan.OutputRoot}";
        CanExecute = true;
    }

    /// <summary>Session 分析完成后回调：更新 UI 展示 + 转发给外部订阅者。</summary>
    private void OnAnalysisCompleted(AnalysisResult result)
    {
        SuccessRate = result.SuccessRate.ToString("P1");
        SourceSummary = string.Join(" · ", result.Parsed
            .GroupBy(p => p.Source)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} {g.Count():N0}"));
        StatusText = $"分析完成：{result.Total} 个文件，成功 {result.Parsed.Count}，失败 {result.Unparsed.Count}";
        _logger.Info(StatusText);
        HasResult = true;
        AnalysisCompleted?.Invoke(result);
    }

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        if (await _folderPicker.PickFolderAsync() is { } dir) SourceDir = dir;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (await _folderPicker.PickFolderAsync() is { } dir) OutputDir = dir;
    }

    [RelayCommand]
    private async Task BrowsePendingAsync()
    {
        if (await _folderPicker.PickFolderAsync() is { } dir) PendingDir = dir;
    }

    // ---- 目录变更实时保存 ----

    /// <summary>从配置回读路径（设置页恢复出厂等全局重置后调用），否则 VM 持有的旧目录会在下次分析时写回配置，使重置失效。</summary>
    public void ReloadPathsFromConfig()
    {
        _suspendPersist = true;
        try
        {
            SourceDir = _config.Paths.SourceDir;
            OutputDir = _config.Paths.OutputDir;
            PendingDir = _config.Paths.PendingDir;
        }
        finally
        {
            _suspendPersist = false;
        }
    }

    partial void OnSourceDirChanged(string value)
    {
        _config.Paths.SourceDir = value;
        if (!_suspendPersist) _state.SaveConfig(notifyChanged: false);
    }

    partial void OnOutputDirChanged(string value)
    {
        _config.Paths.OutputDir = value;
        if (!_suspendPersist) _state.SaveConfig(notifyChanged: false);
        // 计划摘要里的目标根目录是分析时的快照，改目录后需同步刷新，否则 PlanText 仍显示旧目标（或空）
        if (_session.LastResult is { } result)
            _session.SetResult(result, DisplayTarget());
    }

    partial void OnPendingDirChanged(string value)
    {
        _config.Paths.PendingDir = value;
        if (!_suspendPersist) _state.SaveConfig(notifyChanged: false);
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (IsBusy) return;

        _config.Paths.SourceDir = SourceDir;
        _config.Paths.OutputDir = OutputDir;
        IsBusy = true;
        CanExecute = false;
        Progress = 0;

        if (_jobHost is not null)
        {
            // 托管路径（Android）：任务交进程级宿主 + dataSync 前台服务，退后台/锁屏不中断；
            // CTS 归宿主持有，Activity 重建不失效；回调由宿主 marshal 回 UI 线程。
            var sourceDir = SourceDir;
            var target = DisplayTarget();
            _logger.Info($"开始分析 {sourceDir}");
            _jobHost.StartAnalysis(_session, sourceDir, target, CreateAnalysisProgress(),
                onCompleted: () => IsBusy = false,
                onFailed: ex =>
                {
                    StatusText = $"分析失败：{ex.Message}";
                    _logger.Error(StatusText);
                    IsBusy = false;
                },
                onCanceled: () =>
                {
                    StatusText = "分析已取消";
                    IsBusy = false;
                });
            return;
        }

        _cts = new CancellationTokenSource();
        try
        {
            _logger.Info($"开始分析 {SourceDir}");
            await _session.AnalyzeAsync(SourceDir, DisplayTarget(), CreateAnalysisProgress(), _cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "分析已取消";
        }
        catch (Exception ex)
        {
            StatusText = $"分析失败：{ex.Message}";
            _logger.Error(StatusText);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private Progress<AnalysisProgress> CreateAnalysisProgress()
        => new(p =>
        {
            TotalFiles = p.Total;
            SuccessFiles = p.Succeeded;
            FailedFiles = p.Failed;
            Progress = p.Total == 0 ? 0 : (double)p.Processed / p.Total * 100;
            StatusText = $"正在分析 {p.Processed}/{p.Total}…";
        });

    [RelayCommand]
    private void CancelAnalysis()
    {
        // 托管任务取消归宿主（VM 的 _cts 与宿主 CTS 二选一，谁在跑取消谁）
        if (_jobHost?.IsJobRunning == true)
            _jobHost.Cancel();
        else
            _cts?.Cancel();
        StatusText = "正在取消…";
    }

    [RelayCommand]
    private async Task ExecuteFilesAsync()
    {
        // 拦截分支必须给出可见反馈，否则移动端表现为“点了没反应”（FR-A5.6）
        if (IsBusy)
        {
            StatusText = "正在处理中，请稍候…";
            return;
        }

        if (!_session.HasResult)
        {
            StatusText = "尚无分析结果，请先执行分析";
            _logger.Warn("执行已拦截：尚无分析结果（可能是配置变更后计划已失效）");
            return;
        }

        // 执行前预检（FR-A5.6）：本地目标必须先选定输出目录。
        // 否则 Core 侧 LocalFileStorage("") 会抛空路径异常，移动端又没有错误弹窗，表现为"点了没反应"。
        if (IsLocalTarget && string.IsNullOrWhiteSpace(OutputDir))
        {
            StatusText = "请先选择输出目录，再执行归档";
            _logger.Warn($"执行已拦截：{StatusText}（输出目标=本地目录，OutputDir 为空）");
            return;
        }

        // 执行前把当前选项快照进 session（选项变更已即时触发重规划）
        _session.Operation = OperationIndex == 1 ? FileOperation.Move : FileOperation.Copy;
        _session.ExistAction = ExistActionIndex.ToExistAction();
        _session.Level = LevelIndex.ToClassificationLevel();
        _session.FixMtime = FixMtime;

        _config.Execute.Operation = _session.Operation;
        _config.Execute.ExistAction = _session.ExistAction;
        _config.Execute.ClassificationLevel = _session.Level;
        _config.Execute.FixMtime = FixMtime;
        // 仅持久化执行偏好，不触发 Changed → 不失效当前计划（否则执行前计划被清空）
        _state.SaveConfig(notifyChanged: false);

        IsBusy = true;
        Progress = 0;

        if (_jobHost is not null)
        {
            // 托管路径（Android）：同 AnalyzeAsync 注释；取消走 CancelAnalysisCommand → host.Cancel()
            _jobHost.StartExecution(_session, CreateExecuteProgress(),
                onCompleted: result =>
                {
                    StatusText = JobText.DescribeExecution(result);
                    _logger.Info(StatusText);
                    foreach (var err in result.Errors.Take(10))
                        _logger.Warn(err);
                    IsBusy = false;
                },
                onFailed: ex =>
                {
                    StatusText = $"执行失败：{ex.Message}";
                    _logger.Error(StatusText);
                    IsBusy = false;
                },
                onCanceled: () =>
                {
                    StatusText = "执行已取消";
                    IsBusy = false;
                });
            return;
        }

        _cts = new CancellationTokenSource();
        try
        {
            var result = await _session.ExecuteAsync(CreateExecuteProgress(), _cts.Token);

            StatusText = JobText.DescribeExecution(result);
            _logger.Info(StatusText);
            foreach (var err in result.Errors.Take(10))
                _logger.Warn(err);
        }
        catch (OperationCanceledException)
        {
            StatusText = "执行已取消";
        }
        catch (Exception ex)
        {
            StatusText = $"执行失败：{ex.Message}";
            _logger.Error(StatusText);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private Progress<double> CreateExecuteProgress()
    {
        var lastFlush = DateTime.UtcNow;
        return new Progress<double>(p =>
        {
            // 节流（评估文档 P3）：大批量时每文件一次 UI 刷新过频；终值 1.0 恒刷，其余 200ms 一刷
            if (p < 1.0 && (DateTime.UtcNow - lastFlush).TotalMilliseconds < 200)
                return;
            lastFlush = DateTime.UtcNow;
            Progress = p * 100;
            StatusText = $"正在执行 {p:P0}…";
        });
    }

    /// <summary>规则或配置变更后重建提取链（由主 VM 事件接线）。</summary>
    public void RebuildChain()
    {
        _logger.Info("配置/规则已变更，提取链已重建");
        if (HasResult)
        {
            // 已有分析结果是旧配置产物，失效计划，避免按过期日期归档
            _session.InvalidateResult();
            CanExecute = false;
            PlanText = "提取配置已变更，请重新分析后再执行";
            StatusText = "提取配置已变更，请重新分析后再执行";
        }
    }
}
