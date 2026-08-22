using Avalonia.Controls;
using Avalonia.Interactivity;
using MediaOrganizer.Shared.ViewModels;
using Button = Avalonia.Controls.Button;

namespace MediaOrganizer.Android.Views;

/// <summary>
/// 设置（FR-A8）：提取器 / 文件名模式 / 网络位置 / 扫描参数四个分组卡片。
/// 数值型下拉（年份回溯/缓冲天数/并行度）与 VM int 属性的映射在 code-behind 完成。
/// </summary>
public partial class SettingsView : UserControl
{
    private static readonly int[] MaxYearsValues = [20, 30, 50];
    private static readonly int[] FutureBufferValues = [0, 1, 7];
    private static readonly int[] ScanParallelValues = [0, 1, 2, 4, 8];
    private static readonly int[] ExecParallelValues = [0, 1, 2, 4];

    private SettingsViewModel? _vm;
    private bool _syncing;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        Loaded += (_, _) => Subscribe();
    }

    private void Subscribe()
    {
        if (ReferenceEquals(_vm, DataContext) || DataContext is not SettingsViewModel vm) return;
        _vm = vm;
        _syncing = true;
        MaxYearsCombo.SelectedIndex = IndexOf(MaxYearsValues, vm.MaxYearsPast);
        FutureBufferCombo.SelectedIndex = IndexOf(FutureBufferValues, vm.FutureDateBufferDays);
        ScanParallelCombo.SelectedIndex = IndexOf(ScanParallelValues, vm.MaxDegreeOfParallelism);
        ExecParallelCombo.SelectedIndex = IndexOf(ExecParallelValues, vm.ExecutionParallelism);
        _syncing = false;
    }

    private static int IndexOf(int[] values, int value)
    {
        var i = Array.IndexOf(values, value);
        return i < 0 ? 0 : i;
    }

    private void OnMaxYearsChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = MaxYearsCombo.SelectedIndex;
        if (!_syncing && _vm is { } vm && idx >= 0)
            vm.MaxYearsPast = MaxYearsValues[idx];
    }

    private void OnFutureBufferChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = FutureBufferCombo.SelectedIndex;
        if (!_syncing && _vm is { } vm && idx >= 0)
            vm.FutureDateBufferDays = FutureBufferValues[idx];
    }

    private void OnScanParallelChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = ScanParallelCombo.SelectedIndex;
        if (!_syncing && _vm is { } vm && idx >= 0)
            vm.MaxDegreeOfParallelism = ScanParallelValues[idx];
    }

    private void OnExecParallelChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = ExecParallelCombo.SelectedIndex;
        if (!_syncing && _vm is { } vm && idx >= 0)
            vm.ExecutionParallelism = ExecParallelValues[idx];
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
