using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Execution;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Planning;

namespace MediaOrganizer.Desktop.ViewModels;

/// <summary>整理工作台：选目录 → 分析 → 确认计划 → 执行归档。</summary>
public partial class WorkbenchViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly List<PatternDefinition> _patterns;
    private readonly AppLogger _logger;
    private readonly string _configPath;
    private readonly string _patternsPath;

    private AnalysisResult? _result;
    private ArchivePlan? _plan;

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

    public WorkbenchViewModel(AppConfig config, List<PatternDefinition> patterns, AppLogger logger, string configPath, string patternsPath)
    {
        _config = config;
        _patterns = patterns;
        _logger = logger;
        _configPath = configPath;
        _patternsPath = patternsPath;
        _sourceDir = config.Paths.SourceDir;
        _outputDir = config.Paths.OutputDir;
        _fixMtime = config.Execute.FixMtime;
        _operationIndex = config.Execute.Operation == FileOperation.Move ? 1 : 0;
        _existActionIndex = config.Execute.ExistAction switch
        {
            ExistAction.Overwrite => 1,
            ExistAction.Rename => 2,
            _ => 0
        };
        _levelIndex = config.Execute.ClassificationLevel switch
        {
            ClassificationLevel.Month => 1,
            ClassificationLevel.Year => 2,
            _ => 0
        };
    }

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        if (await PickFolderAsync() is { } dir) SourceDir = dir;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (await PickFolderAsync() is { } dir) OutputDir = dir;
    }

    private static async Task<string?> PickFolderAsync()
    {
        var top = App.MainWindow;
        if (top is null) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择目录",
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
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
        try
        {
            var analyzer = CoreFactory.CreateAnalyzer(_config, _patterns);
            var progress = new Progress<AnalysisProgress>(p =>
            {
                TotalFiles = p.Total;
                SuccessFiles = p.Succeeded;
                FailedFiles = p.Failed;
                Progress = p.Total == 0 ? 0 : (double)p.Processed / p.Total * 100;
                StatusText = $"正在分析 {p.Processed}/{p.Total}…";
            });

            _logger.Info($"开始分析 {SourceDir}");
            _result = await analyzer.AnalyzeAsync(SourceDir, progress);

            SuccessRate = _result.SuccessRate.ToString("P1");
            StatusText = $"分析完成：{_result.Total} 个文件，成功 {_result.Parsed.Count}，失败 {_result.Unparsed.Count}";
            _logger.Info(StatusText);
            HasResult = true;

            // 落盘：analysis-result.json + TXT 报告
            try
            {
                AnalysisResultStore.Save(Path.Combine(SourceDir, "analysis-result.json"), _result);
                var report = AnalysisReportGenerator.Generate(_result, OutputDir);
                File.WriteAllText(Path.Combine(SourceDir, "analysis-report.txt"), report);
            }
            catch (Exception ex)
            {
                _logger.Warn($"结果落盘失败：{ex.Message}");
            }

            RebuildPlan();
            AnalysisCompleted?.Invoke(_result);
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
        }
    }

    private void RebuildPlan()
    {
        if (_result is null) return;
        var level = LevelIndex switch
        {
            1 => ClassificationLevel.Month,
            2 => ClassificationLevel.Year,
            _ => ClassificationLevel.Day
        };
        _plan = new ArchivePlanner(level).Plan(_result, OutputDir);
        var op = OperationIndex == 0 ? "复制" : "移动";
        PlanText = $"将对 {_plan.Files.Count} 个文件执行「{op}」到 {OutputDir}";
        CanExecute = true;
    }

    [RelayCommand]
    private async Task ExecuteFilesAsync()
    {
        if (_plan is null || IsBusy) return;

        var op = OperationIndex == 0 ? FileOperation.Copy : FileOperation.Move;
        var exist = ExistActionIndex switch
        {
            1 => ExistAction.Overwrite,
            2 => ExistAction.Rename,
            _ => ExistAction.Skip
        };
        var level = LevelIndex switch
        {
            1 => ClassificationLevel.Month,
            2 => ClassificationLevel.Year,
            _ => ClassificationLevel.Day
        };

        _config.Execute.Operation = op;
        _config.Execute.ExistAction = exist;
        _config.Execute.ClassificationLevel = level;
        _config.Execute.FixMtime = FixMtime;
        ConfigManager.Save(_configPath, _config);

        // 文件操作放到后台线程，UI 保持响应（修复窗口卡死）
        IsBusy = true;
        Progress = 0;
        try
        {
            var executor = new FileOperator(op, exist, FixMtime);
            var progress = new Progress<double>(p =>
            {
                Progress = p * 100;
                StatusText = $"正在执行 {p:P0}…";
            });
            var plan = _plan;
            var result = await Task.Run(() => executor.Execute(plan, progress));

            StatusText = $"执行完成：成功 {result.Succeeded}，跳过 {result.Skipped}，覆盖 {result.Overwritten}，重命名 {result.Renamed}，失败 {result.Failed}";
            _logger.Info(StatusText);
            foreach (var err in result.Errors.Take(10))
                _logger.Warn(err);
        }
        catch (Exception ex)
        {
            StatusText = $"执行失败：{ex.Message}";
            _logger.Error(StatusText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>规则或配置变更后重建提取链（由 MainWindow 事件接线）。</summary>
    public void RebuildChain()
    {
        _logger.Info("配置/规则已变更，提取链已重建");
        if (HasResult)
        {
            // 已有分析结果是旧配置产物，失效计划，避免按过期日期归档
            _plan = null;
            CanExecute = false;
            StatusText = "提取配置已变更，请重新分析后再执行";
        }
    }
}
