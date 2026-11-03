using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Controls;

/// <summary>
/// 触屏选择字段（取代桌面导向的 ComboBox，审查报告2 §2.3 / 报告3 §二）：只读按钮外观显示当前值，
/// 点击弹系统级单选对话框（IOptionPicker → AndroidOptionPicker）。对话框为独立窗口，
/// 与页面 ScrollViewer 完全解耦，从机制上根除 Fluent ComboBox 打开时的 BringIntoView 页面滚动跳动。
/// VM 零改动：Options / SelectedIndex / SelectedValue 与 ComboBox 同语义；
/// SelectedValueMember 指定从选项对象取值的属性名（如 IntOption 的 "Value"），为空时选项对象本身即值。
/// </summary>
public class OptionPicker : Button
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<OptionPicker, string?>(nameof(Title));

    public static readonly StyledProperty<IReadOnlyList<object?>?> OptionsProperty =
        AvaloniaProperty.Register<OptionPicker, IReadOnlyList<object?>?>(nameof(Options));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<OptionPicker, int>(nameof(SelectedIndex), -1);

    public static readonly StyledProperty<object?> SelectedValueProperty =
        AvaloniaProperty.Register<OptionPicker, object?>(nameof(SelectedValue));

    /// <summary>从选项对象取值的属性名（如 IntOption 的 "Value"）；为空 = 选项对象本身即值。</summary>
    public static readonly StyledProperty<string?> SelectedValueMemberProperty =
        AvaloniaProperty.Register<OptionPicker, string?>(nameof(SelectedValueMember));

    private TextBlock? _valueText;
    private bool _syncing;

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IReadOnlyList<object?>? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public string? SelectedValueMember
    {
        get => GetValue(SelectedValueMemberProperty);
        set => SetValue(SelectedValueMemberProperty, value);
    }

    public OptionPicker()
    {
        // 按钮内容：当前值文本 + 右侧下拉箭头（矢量，不依赖字体字形）
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _valueText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var chevron = new PathIcon
        {
            Width = 16,
            Height = 16,
            Data = AppIcons.ChevronDown,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(chevron, 1);
        panel.Children.Add(_valueText);
        panel.Children.Add(chevron);
        Content = panel;
        UpdateDisplay();
    }

    protected override void OnClick()
    {
        base.OnClick();
        _ = PickAsync();
    }

    private async Task PickAsync()
    {
        try
        {
            var picker = App.OptionPicker;
            var options = Options;
            if (picker is null || options is null || options.Count == 0) return;

            var labels = new string[options.Count];
            for (var i = 0; i < options.Count; i++)
                labels[i] = options[i]?.ToString() ?? string.Empty;

            var picked = await picker.PickAsync(Title ?? string.Empty, labels, SelectedIndex);
            if (picked < 0 || picked >= options.Count) return; // 取消 / 返回键 / 点外部
            SelectedIndex = picked; // → SyncFromIndex 回写 SelectedValue
        }
        catch
        {
            // 选择路径异常（对话框层或写回绑定）：保持当前选择，不让 fire-and-forget 变成未观察异常
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_syncing) return;
        if (change.Property == OptionsProperty || change.Property == SelectedValueProperty)
            SyncFromValue();
        else if (change.Property == SelectedIndexProperty)
            SyncFromIndex();
    }

    /// <summary>SelectedValue 外部变更（VM 写回）：按值反查索引并刷新显示。</summary>
    private void SyncFromValue()
    {
        _syncing = true;
        try
        {
            var options = Options;
            if (options is null || options.Count == 0)
            {
                UpdateDisplay();
                return;
            }
            var value = SelectedValue;
            if (value is not null)
            {
                for (var i = 0; i < options.Count; i++)
                {
                    if (!Equals(ExtractValue(options[i]), value)) continue;
                    if (SelectedIndex != i) SelectedIndex = i;
                    break;
                }
            }
            UpdateDisplay();
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>SelectedIndex 变更（VM 写回或本控件选择）：回写值并刷新显示。</summary>
    private void SyncFromIndex()
    {
        _syncing = true;
        try
        {
            var options = Options;
            var idx = SelectedIndex;
            if (options is not null && idx >= 0 && idx < options.Count)
                SelectedValue = ExtractValue(options[idx]);
            UpdateDisplay();
        }
        finally
        {
            _syncing = false;
        }
    }

    private object? ExtractValue(object? item)
    {
        var member = SelectedValueMember;
        if (string.IsNullOrEmpty(member) || item is null) return item;
        return item.GetType().GetProperty(member)?.GetValue(item) ?? item;
    }

    private void UpdateDisplay()
    {
        var options = Options;
        var idx = SelectedIndex;
        var text = options is not null && idx >= 0 && idx < options.Count
            ? options[idx]?.ToString() ?? string.Empty
            : string.Empty;
        if (_valueText is not null) _valueText.Text = text;
    }
}
