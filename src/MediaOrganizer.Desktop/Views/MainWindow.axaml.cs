using Avalonia.Controls;
using MediaOrganizer.Core.Configuration;

namespace MediaOrganizer.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>应用配置中的窗口尺寸（FR-8.4；"WxH" 格式，解析失败用默认）。</summary>
    public void ApplyWindowSize(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.General.WindowSize)) return;
        var parts = config.General.WindowSize.Split('x', 'X');
        if (parts.Length == 2
            && double.TryParse(parts[0], out var w) && w > 0
            && double.TryParse(parts[1], out var h) && h > 0)
        {
            Width = w;
            Height = h;
        }
    }
}
