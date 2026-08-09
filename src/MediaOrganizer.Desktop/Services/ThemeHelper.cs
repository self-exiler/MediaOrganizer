using Avalonia;
using Avalonia.Styling;

namespace MediaOrganizer.Desktop.Services;

/// <summary>运行时主题切换。</summary>
public static class ThemeHelper
{
    public static void Apply(string theme)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
