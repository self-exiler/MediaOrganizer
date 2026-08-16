using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Android.Platforms;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Android.ViewModels;

/// <summary>
/// 抽屉导航（FR-A7.1/FR-A7.2/FR-A7.3）：汉堡菜单切换页面（整理工作台 / 失败文件 / 分析报告 / 设置），
/// 底部状态栏聚合操作反馈，返回键行为（关抽屉/退出）由 View 层处理。
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
    private bool _isBusy;

    [ObservableProperty]
    private bool _drawerOpen;

    [ObservableProperty]
    private int _selectedPage; // 0 工作台 / 1 失败 / 2 报告 / 3 设置

    public ViewModelBase? CurrentPage => SelectedPage switch
    {
        1 => FailedFiles,
        2 => Report,
        3 => Settings,
        _ => Workbench
    };

    public MainViewModel(
        AppState state,
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

    public void RefreshFailedBadge(AnalysisResult? result)
        => FailedBadge = result?.Unparsed.Count ?? 0;

    partial void OnIsBusyChanged(bool value)
    {
        if (value) _wakeLock.Acquire();
        else _wakeLock.Release();
    }

    /// <summary>由 App 订阅 workbench.IsBusy 转发：忙碌时持 WakeLock 并更新状态栏。</summary>
    public void SetBusy(bool busy)
    {
        IsBusy = busy;
        if (busy) StatusText = "正在处理…";
    }
}
