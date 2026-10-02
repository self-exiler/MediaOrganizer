using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MediaOrganizer.Desktop.ViewModels;
using MediaOrganizer.Desktop.Views;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Desktop;

/// <summary>ViewModel → View 定位器。
/// 显式登记表而非命名约定反射：反射解析在 Native AOT 下会被裁剪器静默移除视图类型，
/// 内容区退化成 "Not Found" 文本（工厂委托即静态引用，保证视图进入裁剪根）。</summary>
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Views = new()
    {
        [typeof(WorkbenchViewModel)] = () => new WorkbenchView(),
        [typeof(FailedFilesViewModel)] = () => new FailedFilesView(),
        [typeof(MagicToolsViewModel)] = () => new MagicToolsView(),
        [typeof(ReportViewModel)] = () => new ReportView(),
        [typeof(SettingsViewModel)] = () => new SettingsView(),
        [typeof(LogsViewModel)] = () => new LogsView()
    };

    public Control? Build(object? param)
        => param is null
            ? null
            : Views.TryGetValue(param.GetType(), out var createView)
                ? createView()
                : new TextBlock { Text = "未找到视图：" + param.GetType().FullName };

    public bool Match(object? data) => data is ViewModelBase;
}
