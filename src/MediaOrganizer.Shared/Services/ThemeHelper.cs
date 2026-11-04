using Avalonia;
using Avalonia.Styling;

namespace MediaOrganizer.Shared.Services;

/// <summary>运行时主题切换：桌面与 Android 共用同一份映射（走查 #19）。</summary>
public static class ThemeHelper
{
    public static void Apply(string theme)
    {
        // Android 目标下裸写 Application 与 Android.App.Application 二义（CS0104），全限定
        if (Avalonia.Application.Current is not { } app) return;
        app.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
