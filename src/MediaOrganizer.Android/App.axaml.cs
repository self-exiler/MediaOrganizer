using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Button = Avalonia.Controls.Button;
using MediaOrganizer.Android.Platforms;
using MediaOrganizer.Android.ViewModels;
using MediaOrganizer.Android.Views;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Diagnostics;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Core.Scanning;
using MediaOrganizer.Core.Security;
using MediaOrganizer.Core.Storage;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Android;

public partial class App : Avalonia.Application
{
    private bool _initialized;

    /// <summary>主 VM（抽屉/状态栏/页面切换），MainActivity 返回键拦截读取。</summary>
    public static MainViewModel? Main { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // OnFrameworkInitializationCompleted 不重写：回调在 Application.OnCreate 阶段同步触发，
    // 此时 MainActivity 尚未创建——依赖 Activity 的初始化全部在 MainActivity.OnCreate → InitializeApp() 中完成。

    /// <summary>
    /// 在 MainActivity.OnCreate 中调用（Instance 已就绪）：构建 VM 图、注入平台服务、挂载主视图。
    /// VM 图进程内仅构建一次（Activity 重建时幂等跳过）；主视图经 ISingleViewApplicationLifetime.MainView
    /// 挂载，Activity 每次创建都会经 MainViewFactory 重新渲染。
    /// </summary>
    public void InitializeApp(MainActivity activity)
    {
        if (_initialized) return;
        _initialized = true;

        var dataDir = activity.FilesDir?.AbsolutePath ?? Path.GetTempPath();
        Directory.CreateDirectory(dataDir);
        var configPath = Path.Combine(dataDir, "config.json");
        var patternsPath = Path.Combine(dataDir, "patterns.json");

        var state = AppState.Load(configPath, patternsPath);
        var logger = new AppLogger();
        // 配置加载异常不得静默（AppState 契约）：落日志提醒，损坏副本保留隔离路径供人工恢复
        if (state.LoadError is not null)
            logger.Warn($"配置文件加载异常，已回退默认值：{state.LoadError}");
        if (state.QuarantinedConfigPath is not null)
            logger.Warn($"损坏的配置文件已隔离至：{state.QuarantinedConfigPath}");
        if (state.QuarantinedPatternsPath is not null)
            logger.Warn($"损坏的模式文件已隔离至：{state.QuarantinedPatternsPath}");

        // 平台服务注入：Keystore 凭据 + 双模式本地存储（content: → SAF；真实路径 → System.IO）+ EXIF/选取/导出/确认。
        // 已获全局存储权限时把残留的 content: 树 URI 换算为真实路径（治愈旧配置/持久授权丢失），
        // 输出与待处理目录同样受益（PendingFileMover 经 CreateLocalStorage 创建）。
        CredentialCrypto.Current = new AndroidCredentialCrypto();
        // Core 内部诊断（SMB 协商 MaxWriteSize、FGS 启动失败等）落到应用日志（评估文档 M0/P1）
        CoreLog.Sink = message => logger.Info(message);
        StorageFactory.CustomLocalStorageFactory = path =>
        {
            var real = SafPaths.IsSafIdentifier(path) && AndroidStorageAccess.HasAllFilesAccess
                ? SafPaths.TryTreeUriToPath(path)
                : null;
            return SafPaths.IsSafIdentifier(path) && real is null
                ? new AndroidSafStorage(path, activity.Resolver)
                : new LocalFileStorage(real ?? path);
        };

        var folderPicker = new SafFolderPicker(activity);
        var fileSaver = new AndroidFileSaver(activity);
        var confirm = new AndroidConfirmDialog();
        var opener = new AndroidSystemFileOpener(activity);
        var wakeLock = new WakeLockHolder();

        // 进程级任务宿主（评估文档 M1）：分析/执行交 dataSync 前台服务保活，退后台/锁屏不中断；
        // CTS 随宿主，Activity 重建不失效。VM 图仅构建一次，宿主同样进程内单例。
        var jobHost = new OrganizeJobHost(confirm);
        OrganizeJobHost.Current = jobHost;

        var workbench = new WorkbenchViewModel(
            state, logger, folderPicker,
            () =>
            {
                // 恒用 AndroidFileScanner：其内部已按 content:/真实路径 委托 FileScanner（快扫描或 SAF 遍历），
                // 并自带「目录不存在」的友好报错（直接 new FileScanner 会静默返回空列表）
                return CoreFactory.CreateAnalyzer(state.Config, state.Patterns,
                    new AndroidFileScanner(state.Config.Scan.SupportedFormats, state.Config.Scan.ScanAllFiles, activity.Resolver),
                    new AndroidExifReader());
            },
            dataDir,
            jobHost);

        var failedFiles = new FailedFilesViewModel(state, logger, confirm, opener, new AndroidImageLoader());
        var settings = new SettingsViewModel(state);
        var report = new ReportViewModel(fileSaver);
        var main = new MainViewModel(workbench, failedFiles, settings, report, wakeLock);

        // 事件总线：分析完成 → 失败文件/报告刷新 + 状态栏摘要；规则变更 → 工作台重建链/目标刷新
        workbench.AnalysisCompleted += result =>
        {
            failedFiles.Refresh(result);
            report.Set(result, state.Config.Paths.OutputDir, workbench.LastReportText); // P1-1：复用 Session 报告，避免重复分组统计
            main.RefreshFailedBadge(result);
        };
        state.Changed += workbench.RebuildChain;
        state.Changed += workbench.ReloadPathsFromConfig; // 恢复出厂等重置后回读路径，避免旧目录写回配置
        state.Changed += () => failedFiles.PendingDir = state.Config.Paths.PendingDir;
        state.Changed += workbench.ReloadOutputTargets;
        workbench.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkbenchViewModel.IsBusy))
                main.SetBusy(workbench.IsBusy);
            if (e.PropertyName == nameof(WorkbenchViewModel.StatusText))
                main.StatusText = workbench.StatusText;
        };

        Main = main;

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = new MainView { DataContext = main };
    }
}

/// <summary>
/// 移动端轻量主题：卡片/输入框/按钮的触屏尺寸与配色（对齐 docs/android-prototype，≥48dp 热区）。
/// 以 FluentTheme 为基，仅覆盖控件尺寸与圆角，不引入第三方主题。
/// </summary>
public class AppTheme : Styles
{
    public AppTheme()
    {
        Add(new Style(x => x.Is<Button>())
        {
            Setters =
            {
                new Setter(Button.MinHeightProperty, 44d),
                new Setter(Button.PaddingProperty, new Thickness(16, 10)),
                new Setter(Button.CornerRadiusProperty, new CornerRadius(8)),
                new Setter(Button.FontSizeProperty, 14d),
            }
        });
        Add(new Style(x => x.Is<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.MinHeightProperty, 44d),
                new Setter(TextBox.CornerRadiusProperty, new CornerRadius(8)),
                new Setter(TextBox.FontSizeProperty, 13d),
            }
        });
        Add(new Style(x => x.Is<ComboBox>())
        {
            Setters =
            {
                new Setter(ComboBox.MinHeightProperty, 44d),
                new Setter(ComboBox.FontSizeProperty, 13d),
            }
        });
        Add(new Style(x => x.Is<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.MinHeightProperty, 48d) }
        });
    }
}
