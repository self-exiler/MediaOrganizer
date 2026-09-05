using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Extraction;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Core.Platforms;
using MediaOrganizer.Desktop.Icons;
using MediaOrganizer.Desktop.Services;
using MediaOrganizer.Shared.ViewModels;
using System.Collections.ObjectModel;

namespace MediaOrganizer.Desktop.ViewModels;

public sealed record NavItem(string Title, Geometry Icon, ViewModelBase ViewModel);

/// <summary>主窗口：左侧导航 + 各功能页。共享 VM（工作台/失败文件/报告/设置）来自 MediaOrganizer.Shared。</summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppLogger _logger;
    private ViewModelBase? _currentVm;

    public AppState State { get; }
    public WorkbenchViewModel Workbench { get; }
    public FailedFilesViewModel FailedFiles { get; }
    public MagicToolsViewModel MagicTools { get; }
    public SettingsViewModel Settings { get; }
    public ReportViewModel Report { get; }
    public LogsViewModel Logs { get; }

    /// <summary>当前配置（供主窗口应用窗口尺寸等）。</summary>
    public AppConfig Config => State.Config;

    public ObservableCollection<NavItem> NavItems { get; } = [];

    [ObservableProperty]
    private NavItem? _selectedNav;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    public MainWindowViewModel()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaOrganizer");
        Directory.CreateDirectory(appData);
        var configPath = Path.Combine(appData, "config.json");
        var patternsPath = Path.Combine(appData, "patterns.json");

        State = AppState.Load(configPath, patternsPath);
        _logger = new AppLogger();
        // 配置加载异常不得静默（AppState 契约）：落日志提醒，损坏副本保留隔离路径供人工恢复
        if (State.LoadError is not null)
            _logger.Warn($"配置文件加载异常，已回退默认值：{State.LoadError}");
        if (State.QuarantinedConfigPath is not null)
            _logger.Warn($"损坏的配置文件已隔离至：{State.QuarantinedConfigPath}");
        if (State.QuarantinedPatternsPath is not null)
            _logger.Warn($"损坏的模式文件已隔离至：{State.QuarantinedPatternsPath}");
        ThemeHelper.Apply(State.Config.General.Theme);

        // 桌面平台服务注入共享 VM（ADR-0006 决策 4）：目录选取/另存为/确认/系统打开/缩略图
        var folderPicker = new DesktopFolderPicker();
        Workbench = new WorkbenchViewModel(
            State, _logger, folderPicker,
            () => CoreFactory.CreateAnalyzer(State.Config, State.Patterns, null, new MagickExifReader()),
            appData);
        FailedFiles = new FailedFilesViewModel(State, _logger,
            new DesktopConfirmDialog(), new DesktopSystemFileOpener(), new DesktopImageLoader());
        MagicTools = new MagicToolsViewModel(State, appData);
        Settings = new SettingsViewModel(State, ThemeHelper.Apply);
        Report = new ReportViewModel(new DesktopFileSaver());
        Logs = new LogsViewModel(_logger);

        // 事件总线：分析完成 → 失败文件/报告刷新；规则或配置变更 → 工作台重建提取链
        Workbench.AnalysisCompleted += result =>
        {
            FailedFiles.Refresh(result);
            Report.Set(result, State.Config.Paths.OutputDir, Workbench.LastReportText); // P1-1：复用 Session 报告，避免重复分组统计
            MagicTools.LoadFromAnalysisResult(result); // FR-7.1：魔术工具可用最近分析结果作样本
        };
        State.Changed += Workbench.RebuildChain;
        State.Changed += Workbench.ReloadPathsFromConfig; // 恢复出厂等重置后回读路径，避免旧目录写回配置
        State.Changed += () => FailedFiles.PendingDir = State.Config.Paths.PendingDir;
        State.Changed += Workbench.ReloadOutputTargets; // 网络位置增删 → 工作台目标下拉刷新
        FailedFiles.NavigateToMagic += names =>
        {
            MagicTools.LoadSamples(names);
            SelectedNav = NavItems.First(n => ReferenceEquals(n.ViewModel, MagicTools));
        };
        Settings.NavigateToMagic += () =>
            SelectedNav = NavItems.First(n => ReferenceEquals(n.ViewModel, MagicTools));

        NavItems.Add(new NavItem("整理工作台", IconPaths.Home, Workbench));
        NavItems.Add(new NavItem("失败文件", IconPaths.Warning, FailedFiles));
        NavItems.Add(new NavItem("魔术工具", IconPaths.Sparkle, MagicTools));
        NavItems.Add(new NavItem("分析报告", IconPaths.Document, Report));
        NavItems.Add(new NavItem("设置", IconPaths.Settings, Settings));
        NavItems.Add(new NavItem("日志", IconPaths.List, Logs));

        SelectedNav = NavItems[0];
        _logger.Info("MediaOrganizer 启动完成");
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (_currentVm is not null)
            _currentVm.PropertyChanged -= OnCurrentVmPropertyChanged;

        _currentVm = value?.ViewModel;

        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnCurrentVmPropertyChanged;
            StatusMessage = GetStatusText(_currentVm);
        }
        else
        {
            StatusMessage = "就绪";
        }
    }

    private void OnCurrentVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchViewModel.StatusText)
            or nameof(FailedFilesViewModel.MoveHint)
            or nameof(MagicToolsViewModel.SaveHint))
            StatusMessage = GetStatusText(_currentVm);
    }

    private static string GetStatusText(ViewModelBase? vm) => vm switch
    {
        WorkbenchViewModel w => string.IsNullOrEmpty(w.StatusText) ? "就绪" : w.StatusText,
        FailedFilesViewModel f => string.IsNullOrEmpty(f.MoveHint) ? "就绪" : f.MoveHint,
        MagicToolsViewModel m => string.IsNullOrEmpty(m.SaveHint) ? "就绪" : m.SaveHint,
        SettingsViewModel s => string.IsNullOrEmpty(s.SaveHint) ? "就绪" : s.SaveHint,
        _ => "就绪"
    };
}
