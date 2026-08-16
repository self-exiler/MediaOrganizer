using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MediaOrganizer.Android.Platforms;
using MediaOrganizer.Android.ViewModels;
using MediaOrganizer.Android.Views;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Core.Security;
using MediaOrganizer.Core.Storage;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Android;

public partial class App : Avalonia.Application
{
    private bool _initialized;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 注意：此回调在 Application.OnCreate 阶段（AvaloniaAndroidApplication.SetupWithLifetime）就被同步调用，
        // 此时 MainActivity 尚未创建，MainActivity.Instance 一定为 null。
        // 因此这里绝不能访问 Activity；所有依赖 Activity 的初始化推迟到 MainActivity.OnCreate → InitializeApp()。
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 在 MainActivity.OnCreate 中调用（Instance 已就绪）：构建 VM 图、注入平台服务、挂载主视图。
    /// VM 图进程内仅构建一次（Activity 重建如系统配置变更时幂等跳过），主视图经
    /// ISingleViewApplicationLifetime.MainView 挂载，Activity 每次创建都会重新渲染。
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

        // 平台服务注入（ADR-0006 决策 4/7/8）：Keystore 凭据 + SAF 本地存储 + EXIF/选取/导出/确认
        CredentialCrypto.Current = new AndroidCredentialCrypto();
        StorageFactory.CustomLocalStorageFactory = treeUri => new AndroidSafStorage(treeUri, activity.ContentResolverInstance);

        var folderPicker = new SafFolderPicker(activity);
        var fileSaver = new AndroidFileSaver(activity);
        var confirm = new AndroidConfirmDialog();
        var opener = new AndroidSystemFileOpener(activity);
        var imageLoader = new AndroidImageLoader();
        var wakeLock = new WakeLockHolder();

        var workbench = new WorkbenchViewModel(
            state, logger, folderPicker,
            () =>
            {
                var scanner = new AndroidFileScanner(state.Config.Scan.SupportedFormats, state.Config.Scan.ScanAllFiles, activity.ContentResolverInstance);
                return CoreFactory.CreateAnalyzer(state.Config, state.Patterns, scanner, new AndroidExifReader());
            },
            dataDir);

        var failedFiles = new FailedFilesViewModel(state, logger, confirm, opener, imageLoader);
        var settings = new SettingsViewModel(state);
        var report = new ReportViewModel(fileSaver);
        var main = new MainViewModel(state, workbench, failedFiles, settings, report, wakeLock);

        // 事件总线：分析完成 → 失败文件/报告刷新 + WakeLock 释放；规则变更 → 工作台重建链
        workbench.AnalysisCompleted += result =>
        {
            failedFiles.Refresh(result);
            report.Set(result, state.Config.Paths.OutputDir);
            main.RefreshFailedBadge(result);
        };
        state.Changed += workbench.RebuildChain;
        state.Changed += () => failedFiles.PendingDir = state.Config.Paths.PendingDir;
        state.Changed += workbench.ReloadOutputTargets;
        workbench.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkbenchViewModel.IsBusy))
                main.SetBusy(workbench.IsBusy);
        };

        MainWindowViewModel = main;

        // 主视图经 Avalonia Android 生命周期挂载：base.OnCreate → InitializeAvaloniaView 会调用 MainViewFactory
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = new MainView { DataContext = main };
    }

    public MainViewModel? MainWindowViewModel { get; private set; }
}
