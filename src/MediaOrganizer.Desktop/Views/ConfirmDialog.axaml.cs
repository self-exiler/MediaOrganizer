using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaOrganizer.Desktop.Views;

/// <summary>简单确认对话框（Avalonia 无内置 MessageBox）。</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string title, string message) : this()
    {
        DataContext = new ConfirmViewModel(title, message);
    }

    private void OnConfirm(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }

    /// <summary>模态确认；返回用户是否确认。</summary>
    public static async Task<bool> AskAsync(Window owner, string title, string message)
    {
        var dlg = new ConfirmDialog(title, message);
        if (owner is not null)
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return await dlg.ShowDialog<bool>(owner);
        }
        dlg.Show();
        return true;
    }
}

public sealed class ConfirmViewModel(string title, string message) : ObservableObject
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}
