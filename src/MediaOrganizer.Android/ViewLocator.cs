using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Android;

/// <summary>VM → View 解析：Shared 的 XxxViewModel 与 Android 专属 VM 都映射到 Android.Views.XxxView。</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? param)
    {
        var type = Resolve(param?.GetType());
        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "未找到视图：" + param?.GetType().FullName };
    }

    private static System.Type? Resolve(System.Type? vmType)
    {
        if (vmType is null) return null;
        var viewName = vmType.FullName!
            .Replace(".Shared.ViewModels.", ".Android.Views.")
            .Replace("ViewModel", "View");
        // 视图全部位于本程序集的 MediaOrganizer.Android.Views 命名空间
        return typeof(ViewLocator).Assembly.GetType(viewName);
    }

    public bool Match(object? data)
        => data is ViewModelBase;
}
