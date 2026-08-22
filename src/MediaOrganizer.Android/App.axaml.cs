using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Button = Avalonia.Controls.Button;
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

    /// <summary>主 VM（抽屉/状态栏/页面切换），MainActivity 返回键拦截读取。</summary>
    public static MainViewModel? Main { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 此回调在 Application.OnCreate 阶段（AvaloniaAndroidApplication.SetupWithLifetime）同步触发，
        // 此时 MainActivity 尚未创建——这里不访问 Activity；依赖 Activity 的初始化全部在
        // MainActivity.OnCreate → InitializeApp() 中完成。
        base.OnFrameworkInitializationCompleted();
    }

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

        // 平台服务注入（ADR-0006 决策 4/7/8）：Keystore 凭据 + SAF 本地存储 + EXIF/选取/导出/确认
        CredentialCrypto.Current = new AndroidCredentialCrypto();
        StorageFactory.CustomLocalStorageFactory = treeUri => new AndroidSafStorage(treeUri, activity.Resolver);

        var folderPicker = new SafFolderPicker(activity);
        var fileSaver = new AndroidFileSaver(activity);
        var confirm = new AndroidConfirmDialog();
        var opener = new AndroidSystemFileOpener(activity);
        var wakeLock = new WakeLockHolder();

        var workbench = new WorkbenchViewModel(
            state, logger, folderPicker,
            () =>
            {
                var scanner = new AndroidFileScanner(state.Config.Scan.SupportedFormats, state.Config.Scan.ScanAllFiles, activity.Resolver);
                return CoreFactory.CreateAnalyzer(state.Config, state.Patterns, scanner, new AndroidExifReader());
            },
            dataDir);

        var failedFiles = new FailedFilesViewModel(state, logger, confirm, opener, new AndroidImageLoader());
        var settings = new SettingsViewModel(state);
        var report = new ReportViewModel(fileSaver);
        var main = new MainViewModel(state, workbench, failedFiles, settings, report, wakeLock);

        // 事件总线：分析完成 → 失败文件/报告刷新 + 状态栏摘要；规则变更 → 工作台重建链/目标刷新
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
            if (e.PropertyName == nameof(WorkbenchViewModel.StatusText))
                main.StatusText = workbench.StatusText;
        };

        Main = main;
        MainWindowViewModel = main;

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = new MainView { DataContext = main };
    }

    public MainViewModel? MainWindowViewModel { get; private set; }
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
