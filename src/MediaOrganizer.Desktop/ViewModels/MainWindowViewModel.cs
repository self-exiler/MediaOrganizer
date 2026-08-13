using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Desktop.Icons;
using MediaOrganizer.Desktop.Services;
using System.Collections.ObjectModel;

namespace MediaOrganizer.Desktop.ViewModels;

public sealed record NavItem(string Title, Geometry Icon, ViewModelBase ViewModel);

/// <summary>主窗口：左侧导航 + 各功能页。</summary>
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
        ThemeHelper.Apply(State.Config.General.Theme);

        Workbench = new WorkbenchViewModel(State, _logger);
        FailedFiles = new FailedFilesViewModel(State, _logger);
        MagicTools = new MagicToolsViewModel(State);
        Settings = new SettingsViewModel(State);
        Report = new ReportViewModel();
        Logs = new LogsViewModel(_logger);

        // 事件总线：分析完成 → 失败文件/报告刷新；规则或配置变更 → 工作台重建提取链
        Workbench.AnalysisCompleted += result =>
        {
            FailedFiles.Refresh(result);
            Report.Set(result, State.Config.Paths.OutputDir);
            MagicTools.LoadFromAnalysisResult(result); // FR-7.1：魔术工具可用最近分析结果作样本
        };
        State.Changed += Workbench.RebuildChain;
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
        if (e.PropertyName is "StatusText" or "MoveHint" or "SaveHint")
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
