using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Android.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnTestProfile(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if ((sender as Control)?.DataContext is NetworkProfileVM vm)
            Vm.TestProfileCommand.Execute(vm);
    }

    private void OnEditNetwork(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if ((sender as Control)?.DataContext is NetworkProfileVM vm)
            Vm.BeginEditNetworkCommand.Execute(vm);
    }

    private void OnDeleteNetwork(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if ((sender as Control)?.DataContext is NetworkProfileVM vm)
            Vm.DeleteNetworkCommand.Execute(vm);
    }
}
