using Avalonia.Controls;
using Avalonia.Interactivity;
using MediaOrganizer.Shared.ViewModels;
using Button = Avalonia.Controls.Button;

namespace MediaOrganizer.Android.Views;

/// <summary>
/// 设置（FR-A8）：提取器 / 文件名模式 / 网络位置 / 扫描参数四个分组卡片。
/// 数值型下拉直绑 SettingsViewModel 的选项列表（SelectedValue），网络位置行操作经 code-behind 转发。
/// </summary>
public partial class SettingsView : UserControl
{
    private SettingsViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as SettingsViewModel;
    }

    // ---- 网络位置行操作（DataTemplate 内条目命令经 code-behind 转发） ----

    private void OnTestProfile(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: NetworkProfileVM vm } && _vm is { } s)
            s.TestProfileCommand.Execute(vm);
    }

    private void OnEditProfile(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: NetworkProfileVM vm } && _vm is { } s)
            s.BeginEditNetworkCommand.Execute(vm);
    }

    private void OnDeleteProfile(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: NetworkProfileVM vm } && _vm is { } s)
            s.DeleteNetworkCommand.Execute(vm);
    }

    private void OnTestEditing(object? sender, RoutedEventArgs e)
        => _vm?.TestNetworkCommand.Execute(null);
}
