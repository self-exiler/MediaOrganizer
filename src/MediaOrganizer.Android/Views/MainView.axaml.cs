using Avalonia.Controls;
using Avalonia.Interactivity;
using MediaOrganizer.Android.ViewModels;

namespace MediaOrganizer.Android.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void OnOverlayTapped(object? sender, RoutedEventArgs e)
        => (DataContext as MainViewModel)?.CloseDrawerCommand.Execute(null);
}
