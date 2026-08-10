using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Extraction;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Planning;
using MediaOrganizer.Core.Scanning;

namespace MediaOrganizer.Desktop.ViewModels;

/// <summary>整理工作台：选目录 → 分析 → 确认计划 → 执行归档。</summary>
public partial class WorkbenchViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly List<PatternDefinition> _patterns;
    private readonly AppLogger _logger;
    private readonly AppState _state;
    private readonly OrganizeSession _session;
    private CancellationTokenSource? _cts;

    public event Action<AnalysisResult>? AnalysisCompleted;

    public string[] OperationOptions { get; } = ["复制 copy（保留原文件）", "移动 move"];
    public string[] ExistActionOptions { get; } = ["跳过 skip", "覆盖 overwrite", "重命名 rename（_1、_2…）"];
    public string[] LevelOptions { get; } = ["按日 2024/01/15", "按月 2024/01", "按年 2024"];

    [ObservableProperty]
    private string _sourceDir;

    [ObservableProperty]
    private string _outputDir;

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

    [ObservableProperty]
    private int _outputTargetIndex;

    /// <summary>输出目标选项：第 0 项为本地目录，其余为网络位置（FR-10）。</summary>
    public string[] OutputTargetOptions { get; private set; } = ["本地目录"];

    public WorkbenchViewModel(AppState state, AppLogger logger)
    {
        _state = state;
        _config = state.Config;
        _patterns = state.Patterns;
        _logger = logger;

        _sourceDir = _config.Paths.SourceDir;
        _outputDir = _config.Paths.OutputDir;
        _fixMtime = _config.Execute.FixMtime;
        _operationIndex = _config.Execute.Operation == FileOperation.Move ? 1 : 0;
        _existActionIndex = ExistIndex(_config.Execute.ExistAction);
        _levelIndex = LevelIndexValue(_config.Execute.ClassificationLevel);

        _session = new OrganizeSession(
            _config,
            _config.Execute.ClassificationLevel,
            _config.Execute.Operation,
            _config.Execute.ExistAction,
            _config.Execute.FixMtime);
        _session.PlanChanged += OnPlanChanged;

        ReloadOutputTargets();
    }

    /// <summary>网络位置列表变化后刷新目标下拉并恢复选择（由 MainWindow 接线 State.Changed）。</summary>
    public void ReloadOutputTargets()
    {
        var selected = _config.Paths.OutputNetworkProfile;
        OutputTargetOptions = ["本地目录", .. _config.NetworkProfiles.Select(p => $"网络位置：{p.Name}")];
        OnPropertyChanged(nameof(OutputTargetOptions));
        var newIndex = string.IsNullOrEmpty(selected) ? 0 : Math.Max(0, 1 + _config.NetworkProfiles.FindIndex(p => p.Name == selected));
        if (newIndex != OutputTargetIndex)
            OutputTargetIndex = newIndex; // 触发 OnOutputTargetIndexChanged → 写回配置并刷新网络状态
    }

    partial void OnOutputTargetIndexChanged(int value)
    {
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

    private static int ExistIndex(ExistAction a) => a switch
    {
        ExistAction.Overwrite => 1,
        ExistAction.Rename => 2,
        _ => 0
    };

    private static int LevelIndexValue(ClassificationLevel l) => l switch
    {
        ClassificationLevel.Month => 1,
        ClassificationLevel.Year => 2,
        _ => 0
    };

    private void OnPlanChanged(ArchivePlan plan)
    {
        var op = _session.Operation == FileOperation.Move ? "移动" : "复制";
        PlanText = $"将对 {plan.Files.Count} 个文件执行「{op}」到 {plan.OutputRoot}";
        CanExecute = true;
    }

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        if (await App.PickFolderAsync() is { } dir) SourceDir = dir;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (await App.PickFolderAsync() is { } dir) OutputDir = dir;
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
        _cts = new CancellationTokenSource();
        try
        {
            var analyzer = new Analyzer(
                FileScanner.FromConfig(_config),
                ExtractorChain.FromConfig(_config.Extraction, _patterns),
                _config.Scan.MaxDegreeOfParallelism,
                _config.Scan.ProgressInterval);
            var progress = new Progress<AnalysisProgress>(p =>
            {
                TotalFiles = p.Total;
                SuccessFiles = p.Succeeded;
                FailedFiles = p.Failed;
                Progress = p.Total == 0 ? 0 : (double)p.Processed / p.Total * 100;
                StatusText = $"正在分析 {p.Processed}/{p.Total}…";
            });

            _logger.Info($"开始分析 {SourceDir}");
            var result = await analyzer.AnalyzeAsync(SourceDir, progress, _cts.Token);

            SuccessRate = result.SuccessRate.ToString("P1");
            StatusText = $"分析完成：{result.Total} 个文件，成功 {result.Parsed.Count}，失败 {result.Unparsed.Count}";
            _logger.Info(StatusText);
            HasResult = true;

            // 落盘：analysis-result.json + TXT 报告
            try
            {
                AnalysisResultStore.Save(Path.Combine(SourceDir, "analysis-result.json"), result);
                var report = AnalysisReportGenerator.Generate(result, OutputDir);
                File.WriteAllText(Path.Combine(SourceDir, "analysis-report.txt"), report);
            }
            catch (Exception ex)
            {
                _logger.Warn($"结果落盘失败：{ex.Message}");
            }

            _session.SetResult(result, DisplayTarget());
            AnalysisCompleted?.Invoke(result);
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

    [RelayCommand]
    private void CancelAnalysis()
    {
        _cts?.Cancel();
        StatusText = "正在取消…";
    }

    [RelayCommand]
    private async Task ExecuteFilesAsync()
    {
        if (!_session.HasResult || IsBusy) return;

        // 执行前把当前选项快照进 session（选项变更已即时触发重规划）
        _session.Operation = OperationIndex == 1 ? FileOperation.Move : FileOperation.Copy;
        _session.ExistAction = ExistActionFromIndex(ExistActionIndex);
        _session.Level = LevelFromIndex(LevelIndex);
        _session.FixMtime = FixMtime;

        _config.Execute.Operation = _session.Operation;
        _config.Execute.ExistAction = _session.ExistAction;
        _config.Execute.ClassificationLevel = _session.Level;
        _config.Execute.FixMtime = FixMtime;
        // 仅持久化执行偏好，不触发 Changed → 不失效当前计划（否则执行前计划被清空）
        _state.SaveConfig(notifyChanged: false);

        IsBusy = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p =>
            {
                Progress = p * 100;
                StatusText = $"正在执行 {p:P0}…";
            });
            var result = await _session.ExecuteAsync(progress, _cts.Token);

            StatusText = $"执行完成：成功 {result.Succeeded}，跳过 {result.Skipped}，覆盖 {result.Overwritten}，重命名 {result.Renamed}，失败 {result.Failed}";
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

    private static ExistAction ExistActionFromIndex(int i) => i switch
    {
        1 => ExistAction.Overwrite,
        2 => ExistAction.Rename,
        _ => ExistAction.Skip
    };

    private static ClassificationLevel LevelFromIndex(int i) => i switch
    {
        1 => ClassificationLevel.Month,
        2 => ClassificationLevel.Year,
        _ => ClassificationLevel.Day
    };

    /// <summary>规则或配置变更后重建提取链（由 MainWindow 事件接线）。</summary>
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
