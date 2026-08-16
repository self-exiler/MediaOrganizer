using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Android.Views;

/// <summary>失败文件行包装：勾选/展开为视图本地状态，Item 指向共享 FailedItem。</summary>
public partial class FailedRowVM : ObservableObject
{
    public required FailedItem Item { get; init; }
    public string Name => Item.Name;
    public string Meta => $"大小 {Item.Size:N0} 字节";
    public string FullPath => Item.Path;
    public string Reason => Item.Reason;

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private bool _isExpanded;
}

/// <summary>指纹分组（FR-A6.1）：Items 已按指纹排序，相邻同指纹聚为一组。</summary>
public partial class FailedGroupVM : ObservableObject
{
    public required string Fingerprint { get; init; }
    public string Title => $"指纹 {Fingerprint}（{Rows.Count} 个）";
    public List<FailedRowVM> Rows { get; } = [];
}

public partial class FailedFilesView : UserControl
{
    private FailedFilesViewModel? _vm;

    public FailedFilesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (_vm is not null)
            _vm.Items.CollectionChanged -= OnItemsChanged;
        _vm = DataContext as FailedFilesViewModel;
        if (_vm is null) return;
        _vm.Items.CollectionChanged += OnItemsChanged;
        Rebuild();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>按指纹重建分组；刷新时保留仍存在条目的勾选状态。</summary>
    private void Rebuild()
    {
        if (_vm is null) return;
        var prevChecked = CurrentRows().Where(r => r.IsChecked).Select(r => r.Item).ToHashSet();
        var groups = new List<FailedGroupVM>();
        FailedGroupVM? cur = null;
        foreach (var item in _vm.Items)
        {
            if (cur is null || cur.Fingerprint != item.Fingerprint)
            {
                cur = new FailedGroupVM { Fingerprint = item.Fingerprint };
                groups.Add(cur);
            }
            cur.Rows.Add(new FailedRowVM { Item = item, IsChecked = prevChecked.Contains(item) });
        }
        GroupsList.ItemsSource = groups;
        EmptyHint.IsVisible = _vm.Items.Count == 0;
    }

    private IEnumerable<FailedRowVM> CurrentRows()
        => GroupsList.ItemsSource is IEnumerable<FailedGroupVM> gs ? gs.SelectMany(g => g.Rows) : [];

    private void OnToggleAll(object? sender, RoutedEventArgs e)
    {
        var rows = CurrentRows().ToList();
        if (rows.Count == 0) return;
        var target = !rows.All(r => r.IsChecked);
        foreach (var r in rows)
            r.IsChecked = target;
    }

    private void OnMove(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || _vm.IsMoving) return;
        var selection = CurrentRows().Where(r => r.IsChecked).Select(r => r.Item).ToList();
        // 未勾选任何项时按全量处理（与桌面「全部移到待处理文件夹」语义一致）
        _vm.MoveToPendingCommand.Execute(selection.Count > 0 ? selection : null);
    }

    private void OnRowTapped(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FailedRowVM row)
            row.IsExpanded = !row.IsExpanded;
    }

    private void OnOpenWithSystem(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if ((sender as Control)?.DataContext is not FailedRowVM row) return;
        _vm.Selected = row.Item;
        _vm.OpenWithSystemCommand.Execute(null);
    }
}
