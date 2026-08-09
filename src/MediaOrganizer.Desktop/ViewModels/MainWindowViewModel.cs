using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Desktop.Icons;
using MediaOrganizer.Desktop.Services;

namespace MediaOrganizer.Desktop.ViewModels;

public sealed record NavItem(string Title, Geometry Icon, ViewModelBase ViewModel);

/// <summary>主窗口：左侧导航 + 各功能页。</summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppLogger _logger;
    private readonly AppConfig _config;
    private readonly List<PatternDefinition> _patterns;
    private readonly string _configPath;
    private readonly string _patternsPath;

    public WorkbenchViewModel Workbench { get; }
    public FailedFilesViewModel FailedFiles { get; }
    public MagicToolsViewModel MagicTools { get; }
    public SettingsViewModel Settings { get; }
    public ReportViewModel Report { get; }
    public LogsViewModel Logs { get; }

    public ObservableCollection<NavItem> NavItems { get; } = [];

    [ObservableProperty]
    private NavItem? _selectedNav;

    public MainWindowViewModel()
    {
        var baseDir = AppContext.BaseDirectory;
        _configPath = Path.Combine(baseDir, "config.json");
        _patternsPath = Path.Combine(baseDir, "patterns.json");

        _config = ConfigManager.Load(_configPath);
        _patterns = PatternsStore.Load(_patternsPath);
        _logger = new AppLogger();
        ThemeHelper.Apply(_config.General.Theme);

        Workbench = new WorkbenchViewModel(_config, _patterns, _logger, _configPath, _patternsPath);
        FailedFiles = new FailedFilesViewModel(_config, _logger);
        MagicTools = new MagicToolsViewModel(_patterns, _patternsPath);
        Settings = new SettingsViewModel(_config, _configPath);
        Report = new ReportViewModel();
        Logs = new LogsViewModel(_logger);

        // 事件总线：分析完成 → 失败文件/报告刷新；规则或配置变更 → 工作台重建提取链
        Workbench.AnalysisCompleted += result =>
        {
            FailedFiles.Refresh(result);
            Report.Set(result, _config.Paths.OutputDir);
        };
        MagicTools.PatternsSaved += Workbench.RebuildChain;
        Settings.ConfigSaved += () =>
        {
            Workbench.RebuildChain();
            FailedFiles.PendingDir = _config.Paths.PendingDir;
        };
        FailedFiles.NavigateToMagic += names =>
        {
            MagicTools.LoadSamples(names);
            SelectedNav = NavItems.First(n => ReferenceEquals(n.ViewModel, MagicTools));
        };

        NavItems.Add(new NavItem("整理工作台", IconPaths.Home, Workbench));
        NavItems.Add(new NavItem("失败文件", IconPaths.Warning, FailedFiles));
        NavItems.Add(new NavItem("魔术工具", IconPaths.Sparkle, MagicTools));
        NavItems.Add(new NavItem("分析报告", IconPaths.Document, Report));
        NavItems.Add(new NavItem("设置", IconPaths.Settings, Settings));
        NavItems.Add(new NavItem("日志", IconPaths.List, Logs));

        SelectedNav = NavItems[0];
        _logger.Info("MediaOrganizer 启动完成");
    }
}
