using Avalonia.Controls;
using Avalonia.Interactivity;
using MediaOrganizer.Shared.ViewModels;
using Button = Avalonia.Controls.Button;

namespace MediaOrganizer.Android.Views;

/// <summary>
/// 设置（FR-A8）：外观 / 提取器 / 文件名模式 / 网络位置 / 扫描参数分组卡片。
/// 数值型选择经 OptionPicker 直绑 SettingsViewModel（SelectedValue）；
/// 网络位置行：整行点击进编辑，行尾「⋯」菜单（编辑/测试/删除带确认）经 code-behind 转发。
/// </summary>
public partial class SettingsView : UserControl
{
    private SettingsViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as SettingsViewModel;
    }

    // ---- 提取器权重步进器（审查报告2 M6：触屏精确拖动困难，改 −/+ 步进 0.1） ----

    private void OnWeightDecrement(object? sender, RoutedEventArgs e) => AdjustWeight(sender, -0.1);

    private void OnWeightIncrement(object? sender, RoutedEventArgs e) => AdjustWeight(sender, +0.1);

    private static void AdjustWeight(object? sender, double delta)
    {
        if (sender is Button { DataContext: ExtractorSettingVM vm })
            vm.Weight = Math.Round(Math.Clamp(vm.Weight + delta, 0, 2), 1);
    }

    // ---- 网络位置行操作（DataTemplate 内条目命令经 code-behind 转发） ----

    private void OnEditProfile(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: NetworkProfileVM vm } && _vm is { } s)
            s.BeginEditNetworkCommand.Execute(vm);
    }

    /// <summary>
    /// 行尾「⋯」菜单：编辑 / 测试 / 删除（单选对话框复用 IOptionPicker）。
    /// 删除前系统确认——原实现点击即删且不可恢复（审查报告3 交互安全 P0、报告2 M4）。
    /// </summary>
    private async void OnMoreProfile(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: NetworkProfileVM vm } || _vm is null) return;
        var picker = App.OptionPicker;
        if (picker is null) return;

        try
        {
            string[] actions = ["编辑", "测试连接", "删除"];
            var picked = await picker.PickAsync(vm.Name, actions, -1);
            switch (picked)
            {
                case 0:
                    _vm.BeginEditNetworkCommand.Execute(vm);
                    break;
                case 1:
                    _vm.TestProfileCommand.Execute(vm);
                    break;
                case 2 when App.Confirm is { } confirm:
                    var confirmed = await confirm.ConfirmAsync(
                        "删除网络位置",
                        $"确定删除「{vm.Name}」吗？该操作不可恢复。");
                    if (confirmed)
                        _vm.DeleteNetworkCommand.Execute(vm);
                    break;
            }
        }
        catch
        {
            // 对话框层异常（Activity 销毁等）：丢弃本次操作，保持现状
        }
    }

    private void OnTestEditing(object? sender, RoutedEventArgs e)
        => _vm?.TestNetworkCommand.Execute(null);
}
