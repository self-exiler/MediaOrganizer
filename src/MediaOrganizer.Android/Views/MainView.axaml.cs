using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MediaOrganizer.Android.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void OnOverlayTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.CloseDrawerCommand.Execute(null);
    }
}
