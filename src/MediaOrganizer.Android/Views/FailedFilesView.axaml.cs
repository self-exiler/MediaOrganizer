using Avalonia.Controls;
using Avalonia.Interactivity;
using MediaOrganizer.Shared.ViewModels;
using Button = Avalonia.Controls.Button;

namespace MediaOrganizer.Android.Views;

public partial class FailedFilesView : UserControl
{
    public FailedFilesView()
    {
        InitializeComponent();
    }

    /// <summary>详情内「用系统应用打开」：对当前条目执行 OpenWithSystem（带参数，FR-A6.3）。</summary>
    private void OnOpenItem(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FailedItem item } && DataContext is FailedFilesViewModel vm)
            vm.OpenWithSystemCommand.Execute(item);
    }
}
