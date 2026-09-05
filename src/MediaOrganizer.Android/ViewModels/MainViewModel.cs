using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Android.Platforms;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Android.ViewModels;

/// <summary>抽屉导航项（FR-A7.1）：标签 + 页索引 + 失败文件角标（Badge=0 时角标隐藏）。
/// 可变 ObservableObject：角标变化时原地更新属性，避免替换实例破坏 ListBox 选中状态（P1-8）。</summary>
public sealed partial class NavItem : ObservableObject
{
    public NavItem(string label, int page)
    {
        Label = label;
        Page = page;
    }

    public string Label { get; }
    public int Page { get; }

    [ObservableProperty]
    private int _badge;

    public bool HasBadge => Badge > 0;

    partial void OnBadgeChanged(int value) => OnPropertyChanged(nameof(HasBadge));
}

/// <summary>
/// 应用外壳 VM（FR-A7）：顶部 AppBar 标题随页面切换、侧滑抽屉（整理工作台 / 失败文件 / 分析报告 / 设置）、
/// 底部状态栏聚合操作反馈（忙碌圆点 + 状态文本 + 上次分析摘要）。
/// 返回键行为（关抽屉/退出）由 MainActivity 的 OnBackPressedCallback 处理；
/// WakeLock 随分析/执行的忙碌状态自动持有/释放（NFR-A9）。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    public WorkbenchViewModel Workbench { get; }
    public FailedFilesViewModel FailedFiles { get; }
    public SettingsViewModel Settings { get; }
    public ReportViewModel Report { get; }

    private readonly WakeLockHolder _wakeLock;

    [ObservableProperty]
    private string _appTitle = "整理工作台";

    [ObservableProperty]
    private int _failedBadge;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _statusDetail = "尚未分析";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _drawerOpen;

    [ObservableProperty]
    private int _selectedPage; // 0 工作台 / 1 失败 / 2 报告 / 3 设置

    /// <summary>抽屉导航项（失败文件项带角标，角标变化时替换条目触发刷新）。</summary>
    public ObservableCollection<NavItem> NavItems { get; } =
    [
        new NavItem("整理工作台", 0),
        new NavItem("失败文件", 1) { Badge = 0 },
        new NavItem("分析报告", 2),
        new NavItem("设置", 3)
    ];

    public ViewModelBase? CurrentPage => SelectedPage switch
    {
        1 => FailedFiles,
        2 => Report,
        3 => Settings,
        _ => Workbench
    };

    public MainViewModel(
        WorkbenchViewModel workbench,
        FailedFilesViewModel failedFiles,
        SettingsViewModel settings,
        ReportViewModel report,
        WakeLockHolder wakeLock)
    {
        Workbench = workbench;
        FailedFiles = failedFiles;
        Settings = settings;
        Report = report;
        _wakeLock = wakeLock;
    }

    partial void OnSelectedPageChanged(int value)
    {
        AppTitle = value switch
        {
            1 => "失败文件",
            2 => "分析报告",
            3 => "设置",
            _ => "整理工作台"
        };
        OnPropertyChanged(nameof(CurrentPage));
    }

    [RelayCommand]
    private void ToggleDrawer() => DrawerOpen = !DrawerOpen;

    [RelayCommand]
    private void CloseDrawer() => DrawerOpen = false;

    [RelayCommand]
    private void Navigate(int page)
    {
        SelectedPage = page;
        DrawerOpen = false;
    }

    partial void OnFailedBadgeChanged(int value)
        => NavItems[1].Badge = value;

    public void RefreshFailedBadge(AnalysisResult? result)
    {
        FailedBadge = result?.Unparsed.Count ?? 0;
        StatusDetail = result is null
            ? "尚未分析"
            : $"上次分析: {result.Total:N0} 文件 · 成功 {result.Parsed.Count:N0}";
    }

    partial void OnIsBusyChanged(bool value)
    {
        if (value) _wakeLock.Acquire();
        else _wakeLock.Release();
    }

    /// <summary>
    /// 由 App 订阅 workbench.IsBusy 转发：忙碌时持 WakeLock（OnIsBusyChanged）。
    /// 注意：此处不可重置 StatusText——忙碌结束晚于结果/错误文本写入，会把「执行完成 / 执行失败：…」冲掉，
    /// 表现为操作"点了没反应"。状态栏文本统一由 workbench.StatusText 转发。
    /// </summary>
    public void SetBusy(bool busy) => IsBusy = busy;
}
