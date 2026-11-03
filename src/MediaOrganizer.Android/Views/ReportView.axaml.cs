using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MediaOrganizer.Shared.ViewModels;
using Button = Avalonia.Controls.Button;

namespace MediaOrganizer.Android.Views;

public partial class ReportView : UserControl
{
    public ReportView()
    {
        InitializeComponent();
    }

    /// <summary>「复制文本」：报告全文写入系统剪贴板，按钮短暂反馈（审查报告3 次要建议）。</summary>
    private async void OnCopyReport(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not ReportViewModel { ReportText: { Length: > 0 } text }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        try
        {
            // Avalonia 12 移除了 IClipboard.SetTextAsync 实例方法（v12 破坏性变更），
            // 改经 ClipboardExtensions.SetTextAsync 扩展（Avalonia.Input 命名空间）
            await ClipboardExtensions.SetTextAsync(clipboard, text);
        }
        catch
        {
            // 剪贴板写入失败静默即可：按钮保持原状，用户可改用另存/分享
            return;
        }

        button.Content = "已复制";
        button.IsEnabled = false;
        var restore = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        restore.Tick += (_, _) =>
        {
            restore.Stop();
            button.Content = "复制文本";
            button.IsEnabled = true;
        };
        restore.Start();
    }
}
