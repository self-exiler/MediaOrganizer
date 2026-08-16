using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MediaOrganizer.Shared.ViewModels;
using System.Diagnostics.CodeAnalysis;

namespace MediaOrganizer.Desktop;

/// <summary>ViewModel → View 定位器。约定：ViewModels.XxxViewModel ↔ Views.XxxView；
/// 共享层 VM（MediaOrganizer.Shared.ViewModels）映射到桌面 Views（Android 项目有自己的 ViewLocator）。</summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!
            .Replace("MediaOrganizer.Shared.ViewModels", "MediaOrganizer.Desktop.Views", StringComparison.Ordinal)
            .Replace("ViewModels.", "Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
