using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Avalonia.Android;
using MediaOrganizer.Android.Platforms;

namespace MediaOrganizer.Android;

/// <summary>请求码常量：SAF 选取 / 另存为 / 通知权限。</summary>
public static class RequestCodes
{
    public const int FolderPick = 0x1001;
    public const int CreateDocument = 0x1002;
    public const int PostNotifications = 0x1003;
    public const int AllFilesAccess = 0x1004;
}

/// <summary>
/// Avalonia Android 应用入口（ADR-0005）：AppBuilder 仅做主题与字体定制，
/// VM 图与平台服务注入推迟到 MainActivity.OnCreate → App.InitializeApp（此时 Activity 才可用）。
/// </summary>
[Application]
public class Application(IntPtr javaReference, JniHandleOwnership transfer) : AvaloniaAndroidApplication<App>(javaReference, transfer)
{
    public override void OnCreate()
    {
        // 崩溃落盘钩子必须在一切初始化之前（Honor 机型加密 logcat，只能自建现场日志）
        CrashLogger.Install(this);
        base.OnCreate();
        // 通知渠道：前台服务（dataSync）进度/完成通知的载体，App 启动即创建
        NotificationChannels.EnsureCreated(this);
    }

}

/// <summary>
/// 单一入口 Activity。ConfigurationChanges 全量声明避免旋转时重建；
/// SAF 回调（ACTION_OPEN_DOCUMENT_TREE / CREATE_DOCUMENT）经 StartForResultAsync 路由回 .NET Task。
/// </summary>
[Activity(
    Label = "MediaOrganizer",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
        | ConfigChanges.KeyboardHidden | ConfigChanges.SmallestScreenSize)]
public class MainActivity : AvaloniaMainActivity
{
    private readonly Dictionary<int, Action<Intent?>> _pending = new();
    private DrawerBackCallback? _backCallback;

    public static MainActivity? Instance { get; private set; }

    public ContentResolver Resolver => ContentResolver!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Instance 与 VM 图必须先于 base.OnCreate 就位：base.OnCreate → InitializeAvaloniaView
        // 渲染主视图时 App.InitializeApp 已经完成（App.InitializeApp 在此之前调用）。
        Instance = this;
        ((App)Avalonia.Application.Current!).InitializeApp(this);
        base.OnCreate(savedInstanceState);

        // FR-A7.3：返回键——抽屉打开时仅关抽屉（callback 启用期间拦截）；关闭时走系统默认（退出）
        _backCallback = new DrawerBackCallback();
        OnBackPressedDispatcher.AddCallback(this, _backCallback);
        if (App.Main is { } main)
        {
            SyncBackCallback(main.DrawerOpen);
            main.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ViewModels.MainViewModel.DrawerOpen))
                    SyncBackCallback(main.DrawerOpen);
            };
        }

        // Android 13+ 通知运行时权限（SupportedOS=33 全量命中）：前台服务进度通知的可见性依赖它，
        // 未授权不影响任务运行本身（通知静默丢弃，App 内进度仍可见），故启动时静默申请一次。
        if (CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            RequestPermissions([global::Android.Manifest.Permission.PostNotifications], RequestCodes.PostNotifications);
    }

    private void SyncBackCallback(bool drawerOpen) => _backCallback!.Enabled = drawerOpen;

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this) Instance = null;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (_pending.Remove(requestCode, out var cb))
            cb(resultCode == Result.Ok ? data : null);
    }

    /// <summary>启动一次需要回调的 Activity（SAF 选取等），回调在 UI 线程触发。
    /// 不设超时：Activity 结果回调可靠，等待时间长（用户翻目录）不应导致进程崩溃。</summary>
    public Task<Intent?> StartForResultAsync(Intent intent, int requestCode)
    {
        var tcs = new TaskCompletionSource<Intent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestCode] = data => tcs.TrySetResult(data);
        RunOnUiThread(() => StartActivityForResult(intent, requestCode));
        return tcs.Task;
    }

    /// <summary>返回键拦截：仅在抽屉打开时启用（Enabled 由 MainActivity 按 DrawerOpen 同步）。</summary>
    private sealed class DrawerBackCallback : AndroidX.Activity.OnBackPressedCallback
    {
        public DrawerBackCallback() : base(false) { }

        public override void HandleOnBackPressed()
            => App.Main?.CloseDrawerCommand.Execute(null);
    }
}
