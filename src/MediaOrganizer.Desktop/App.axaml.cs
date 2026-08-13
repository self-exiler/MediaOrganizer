using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MediaOrganizer.Desktop.ViewModels;
using MediaOrganizer.Desktop.Views;

namespace MediaOrganizer.Desktop;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    /// <summary>共享目录选择器（多个 ViewModel 复用）。</summary>
    public static async Task<string?> PickFolderAsync()
    {
        var top = MainWindow;
        if (top is null) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择目录",
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm };
            window.ApplyWindowSize(vm.Config);
            MainWindow = window;
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
