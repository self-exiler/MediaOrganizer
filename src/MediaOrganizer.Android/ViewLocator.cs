using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MediaOrganizer.Shared.ViewModels;
using System.Diagnostics.CodeAnalysis;

namespace MediaOrganizer.Android;

/// <summary>Android ViewModel → View 定位器。共享层 VM（MediaOrganizer.Shared.ViewModels）
/// 映射到 Android Views，Android 自身 VM（抽屉壳）按 ViewModels.↔Views. 约定解析。</summary>
[RequiresUnreferencedCode("ViewLocator uses reflection to resolve views.")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null) return null;
        var name = param.GetType().FullName!
            .Replace("MediaOrganizer.Shared.ViewModels", "MediaOrganizer.Android.Views", StringComparison.Ordinal)
            .Replace("ViewModels.", "Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        return type != null ? (Control)Activator.CreateInstance(type)! : new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
