using Avalonia;

namespace MediaOrganizer.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    // 显式绑定 Win32 + Skia，替代 UsePlatformDetect()：后者会反射探测 X11 / Native 后端，
    // 本项目只发 win-x64，探测纯属启动期浪费。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseWin32()
            .UseSkia()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
