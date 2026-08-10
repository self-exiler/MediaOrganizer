using Avalonia;
using Avalonia.Controls;

namespace MediaOrganizer.Desktop.Views;

public partial class MagicToolsView : UserControl
{
    // 宽/窄屏切换阈值：内容区小于此宽度时样本列表与编辑区上下堆叠
    private const double NarrowThreshold = 900;

    public MagicToolsView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyLayout(Bounds.Width);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            ApplyLayout(Bounds.Width);
    }

    private void ApplyLayout(double width)
    {
        if (MainGrid is null || SamplePanel is null || ContentPanel is null || Splitter is null)
            return;

        if (width < NarrowThreshold)
        {
            // 窄屏：样本在上，编辑测试在下，占满宽度
            MainGrid.ColumnDefinitions.Clear();
            MainGrid.RowDefinitions = new RowDefinitions("Auto,*");

            Grid.SetColumn(SamplePanel, 0);
            Grid.SetRow(SamplePanel, 0);
            Grid.SetColumnSpan(SamplePanel, 1);
            Grid.SetRowSpan(SamplePanel, 1);

            Grid.SetColumn(Splitter, 0);
            Grid.SetRow(Splitter, 0);
            Splitter.IsVisible = false;

            Grid.SetColumn(ContentPanel, 0);
            Grid.SetRow(ContentPanel, 1);
            Grid.SetColumnSpan(ContentPanel, 1);
            Grid.SetRowSpan(ContentPanel, 1);
        }
        else
        {
            // 宽屏：左侧样本，右侧编辑测试
            MainGrid.RowDefinitions.Clear();
            MainGrid.ColumnDefinitions = new ColumnDefinitions("260,Auto,*");

            Grid.SetColumn(SamplePanel, 0);
            Grid.SetRow(SamplePanel, 0);
            Grid.SetColumnSpan(SamplePanel, 1);
            Grid.SetRowSpan(SamplePanel, 1);

            Grid.SetColumn(Splitter, 1);
            Grid.SetRow(Splitter, 0);
            Splitter.IsVisible = true;

            Grid.SetColumn(ContentPanel, 2);
            Grid.SetRow(ContentPanel, 0);
            Grid.SetColumnSpan(ContentPanel, 1);
            Grid.SetRowSpan(ContentPanel, 1);
        }
    }
}
