using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MediaOrganizer.Android.Views;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Android;

/// <summary>VM → View 解析：与桌面端同为显式静态登记表，避免反射解析被裁剪（走查 #19）。</summary>
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Views = new()
    {
        [typeof(WorkbenchViewModel)] = () => new WorkbenchView(),
        [typeof(FailedFilesViewModel)] = () => new FailedFilesView(),
        [typeof(ReportViewModel)] = () => new ReportView(),
        [typeof(SettingsViewModel)] = () => new SettingsView()
    };

    public Control Build(object? param)
        => param is not null && Views.TryGetValue(param.GetType(), out var createView)
            ? createView()
            : new TextBlock { Text = "未找到视图：" + param?.GetType().FullName };

    public bool Match(object? data) => data is ViewModelBase;
}
