using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Android.Platforms;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Android.ViewModels;

/// <summary>
/// 应用外壳 VM（FR-A7）：顶部 AppBar 标题随页面切换、侧滑抽屉（整理工作台 / 失败文件 / 分析报告 / 设置）。
/// 抽屉的 4 个导航项已在 MainView 里改为静态按钮（见该文件注释），此处不再持有导航项集合。
/// 操作反馈经轻提示（Toast）：状态文本变化且非忙碌时由 MainView 弹出、3.5s 自动消失
/// （审查报告1 #8：常驻底部状态栏移除，忙碌进度留在工作台/结果卡片内）。
/// 返回键行为（关抽屉/回工作台/退出）由 MainActivity 的 OnBackPressedCallback 处理；
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

    /// <summary>抽屉底部版本行（如 "v1.2.0-android · 开源 MIT"）。versionName 由 release.yml 以
    /// -p:ApplicationDisplayVersion=&lt;tag&gt; 写进 APK 清单，运行时经 PackageManager 读取，
    /// 与系统安装页/应用信息显示的版本天然一致——不要改回写死字符串，否则 tag 升级后 UI 不同步。</summary>
    public string VersionText { get; } = BuildVersionText();

    private static string BuildVersionText()
    {
        string? version = null;
        try
        {
            var context = global::Android.App.Application.Context;
            version = context.PackageManager?
                .GetPackageInfo(context.PackageName!, 0)?
                .VersionName;
        }
        catch
        {
            // 设计器预览/测试宿主无 Android 运行时：退回程序集元数据（= 构建时 ApplicationDisplayVersion）
        }

        if (string.IsNullOrEmpty(version))
        {
            version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
        }

        if (version is not null)
        {
            var plus = version.IndexOf('+'); // InformationalVersion 可能带 SemVer 元数据后缀
            if (plus >= 0)
            {
                version = version[..plus];
            }
        }

        return $"v{version ?? "?"}-android · 开源 MIT";
    }

    [ObservableProperty]
    private int _selectedPage; // 0 工作台 / 1 失败 / 2 报告 / 3 设置

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
        DrawerOpen = false; // 抽屉 ListBox 选中导航后自动收起（此前仅点遮罩才关，见 FR-A7.1）
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
    {
        FailedBadge = result?.Unparsed.Count ?? 0;
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
