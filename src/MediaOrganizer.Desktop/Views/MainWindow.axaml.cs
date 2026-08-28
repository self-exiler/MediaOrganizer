using Avalonia.Controls;
using Avalonia.Platform;
using MediaOrganizer.Core.Configuration;

namespace MediaOrganizer.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
    }

    private void SetWindowIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://MediaOrganizer.Desktop/Assets/app-icon.ico"));
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // 图标缺失或损坏时保持默认，不阻塞启动
        }
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
