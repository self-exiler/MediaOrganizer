using Avalonia.Controls;
using Avalonia.Interactivity;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Android.Views;

/// <summary>
/// 整理工作台：目录选取 → 操作参数 → 分析 → 统计摘要 → 执行（FR-A4/A5，对齐原型三步指示与三态布局）。
/// 步骤指示（选目录/分析/执行）的状态类由 code-behind 根据 VM 的 IsBusy/HasResult/SourceDir 刷新。
/// </summary>
public partial class WorkbenchView : UserControl
{
    public WorkbenchView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        Loaded += (_, _) => Subscribe();
    }

    private WorkbenchViewModel? _vm;

    private void Subscribe()
    {
        if (ReferenceEquals(_vm, DataContext) || DataContext is not WorkbenchViewModel vm) return;
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateSteps();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchViewModel.IsBusy)
            or nameof(WorkbenchViewModel.HasResult)
            or nameof(WorkbenchViewModel.SourceDir))
            UpdateSteps();
    }

    /// <summary>步骤状态：1 选目录（有源目录=done，否则 cur）；2 分析（HasResult=done，IsBusy=cur）；3 执行（HasResult=cur）。</summary>
    private void UpdateSteps()
    {
        var vm = _vm;
        if (vm is null) return;

        var step1 = string.IsNullOrEmpty(vm.SourceDir) ? "cur" : "done";
        var step2 = vm.IsBusy ? "cur" : vm.HasResult ? "done" : "";
        var step3 = vm.HasResult && !vm.IsBusy ? "cur" : "";

        SetStep(Step1Circle, Step1Label, step1);
        SetStep(Step2Circle, Step2Label, step2);
        SetStep(Step3Circle, Step3Label, step3);
    }

    private static void SetStep(Border circle, TextBlock label, string state)
    {
        circle.Classes.Clear();
        label.Classes.Clear();
        circle.Classes.Add("stepCircle");
        label.Classes.Add("stepLabel");
        if (state.Length > 0)
        {
            circle.Classes.Add(state);
            label.Classes.Add(state);
        }
    }

    /// <summary>「查看失败 →」跳转失败文件页（经 App 主 VM 导航）。</summary>
    private void OnViewFailed(object? sender, RoutedEventArgs e)
        => App.Main?.NavigateCommand.Execute(1);
}
