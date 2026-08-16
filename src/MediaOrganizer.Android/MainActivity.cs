using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MediaOrganizer.Android.Platforms;
using MediaOrganizer.Android.ViewModels;
using MediaOrganizer.Android.Views;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Core.Security;
using MediaOrganizer.Core.Storage;
using MediaOrganizer.Shared.Services;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Android;

/// <summary>请求码常量：SAF 选取/另存为/打开。</summary>
public static class RequestCodes
{
    public const int FolderPick = 0x1001;
    public const int CreateDocument = 0x1002;
    public const int OpenDocument = 0x1003;
}

[Application]
public class Application : AvaloniaAndroidApplication<App>
{
    public Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        => base.CustomizeAppBuilder(builder).WithInterFont();
}

[Activity(
    Label = "MediaOrganizer",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
        | ConfigChanges.KeyboardHidden | ConfigChanges.SmallestScreenSize)]
public class MainActivity : AvaloniaMainActivity
{
    private readonly Dictionary<int, Action<Intent?>> _pending = new();

    public static MainActivity? Instance { get; private set; }
    public ContentResolver ContentResolverInstance => ContentResolver!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Instance 必须先于 base.OnCreate 就位：base.OnCreate → InitializeAvaloniaView 会调用
        // MainViewFactory 渲染主视图；App.InitializeApp 在 base.OnCreate 之前构建 VM 图并挂载主视图。
        Instance = this;
        ((App)Avalonia.Application.Current).InitializeApp(this);
        base.OnCreate(savedInstanceState);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Instance = null;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (_pending.Remove(requestCode, out var cb))
            cb(resultCode == Result.Ok ? data : null);
    }

    /// <summary>启动一次需要回调的 Activity（SAF 选取等），回调在 UI 线程触发。</summary>
    public Task<Intent?> StartForResultAsync(Intent intent, int requestCode)
    {
        var tcs = new TaskCompletionSource<Intent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestCode] = data => tcs.TrySetResult(data);
        RunOnUiThread(() => StartActivityForResult(intent, requestCode));
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
