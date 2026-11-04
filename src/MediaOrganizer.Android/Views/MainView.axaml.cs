using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using MediaOrganizer.Android.ViewModels;
// Android 项目自动生成的全局 using 含 Android.Widget，裸写 Button 与 Avalonia.Controls.Button 二义（CS0104），
// 与 Controls/OptionPicker.cs 的既有别名写法保持一致
using Button = Avalonia.Controls.Button;

namespace MediaOrganizer.Android.Views;

/// <summary>
/// 应用外壳（FR-A7）：AppBar + 4 个常驻页面 + 抽屉（滑入动画/左缘手势/点遮罩关闭）+ 操作反馈轻提示。
/// 安全区：接管 Avalonia 的自动 padding，AppBar 固定 56dp（不叠 top inset），内容区与 Toast 在
/// 底部让出手势导航条（审查报告2 M1：Android 15+ edge-to-edge 下不被遮挡）。
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>左缘手势起触点：按点距左边缘 ≤24px 才记录（审查报告3 抽屉手势）。</summary>
    private const double EdgeSlop = 24;
    /// <summary>触发开抽屉的水平位移阈值。</summary>
    private const double EdgeSwipeThreshold = 60;
    /// <summary>底部手势条 inset 上限（dp）：仅用于压住 ROM 的异常上报，见 ApplyInsets。</summary>
    private const double MaxVerticalInset = 48;

    private IInsetsManager? _insetsManager;
    private DispatcherTimer? _toastTimer;
    private Point? _edgePress;
    private MainViewModel? _vm;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindViewModel();

        // 左缘滑动开抽屉：隧道路由先于 ScrollViewer 处理，只跟踪贴边起触的横向拖动
        Root.AddHandler(PointerPressedEvent, OnRootPointerPressed, RoutingStrategies.Tunnel);
        Root.AddHandler(PointerMovedEvent, OnRootPointerMoved, RoutingStrategies.Tunnel);
        Root.AddHandler(PointerReleasedEvent, OnRootPointerReleased, RoutingStrategies.Tunnel);
    }

    private void BindViewModel()
    {
        if (ReferenceEquals(_vm, DataContext)) return;
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmPropertyChanged;
        UpdatePages(_vm.SelectedPage);
        UpdateBadge(_vm.FailedBadge);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedPage) && _vm is { } vm)
            UpdatePages(vm.SelectedPage);
        else if (e.PropertyName == nameof(MainViewModel.FailedBadge) && _vm is { } badgeVm)
            UpdateBadge(badgeVm.FailedBadge);
        else if (e.PropertyName is nameof(MainViewModel.StatusText) or nameof(MainViewModel.IsBusy))
            UpdateToast();
    }

    // ---- 页面切换（缓存视图 + 淡入） ----

    private void UpdatePages(int page)
    {
        ShowPage(PageWorkbench, page == 0);
        ShowPage(PageFailed, page == 1);
        ShowPage(PageReport, page == 2);
        ShowPage(PageSettings, page == 3);
        UpdateNavSelection(page);
    }

    /// <summary>选中态由静态导航按钮自己承载（不再依赖 ListBox 的 SelectedIndex）。</summary>
    private void UpdateNavSelection(int page)
    {
        Button[] navs = [Nav0, Nav1, Nav2, Nav3];
        for (var i = 0; i < navs.Length; i++)
        {
            if (i == page) navs[i].Classes.Add("selected");
            else navs[i].Classes.Remove("selected");
        }
    }

    /// <summary>失败文件角标：Badge=0 时隐藏。</summary>
    private void UpdateBadge(int count)
    {
        NavBadge.IsVisible = count > 0;
        NavBadgeText.Text = count.ToString();
    }

    private static void ShowPage(ContentControl page, bool visible)
    {
        if (page.IsVisible == visible) return;
        page.IsVisible = visible;
        if (!visible) return;
        // 淡入用一次性动画（审查报告2 F6）：IsVisible 恢复的同一布局周期内改 Opacity，
        // Transitions 过渡不会播放（起步值已是 1），故显式 0→1
        var fadeIn = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(150),
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 1d) } }
            }
        };
        _ = fadeIn.RunAsync(page);
    }

    // ---- 操作反馈轻提示（替代常驻状态栏，FR-A7.2 修订） ----

    private void UpdateToast()
    {
        var vm = _vm;
        if (vm is null) return;
        // 忙碌中的进度文本留在卡片内，Toast 不闪动；忙碌结束后的终态文本（完成/失败/拦截原因）弹出
        if (vm.IsBusy || string.IsNullOrEmpty(vm.StatusText))
        {
            HideToast();
            return;
        }
        ToastText.Text = vm.StatusText;
        Toast.Opacity = 1;
        if (_toastTimer is null)
        {
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            _toastTimer.Tick += (_, _) => HideToast();
        }
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        Toast.Opacity = 0;
        _toastTimer?.Stop();
    }

    // ---- 安全区 insets（审查报告2 M1 / 报告3 F6） ----

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        var insets = topLevel?.InsetsManager;
        if (topLevel is null || insets is null) return;
        // 关闭自动 padding（作用于根，会让 AppBar 色带上端露出窗口底色），改为手动接管；
        // 只让内容区/Toast 按左右+底部 inset 让位，顶部不再叠加（见 ApplyInsets）
        TopLevel.SetAutoSafeAreaPadding(topLevel, false);
        insets.SafeAreaChanged += OnSafeAreaChanged;
        _insetsManager = insets;
        ApplyInsets(insets.SafeAreaPadding);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_insetsManager is not null)
        {
            _insetsManager.SafeAreaChanged -= OnSafeAreaChanged;
            _insetsManager = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => ApplyInsets(e.SafeAreaPadding);

    private void ApplyInsets(Thickness inset)
    {
        // 顶部蓝色带被撑成近 200dp 的真机问题：关闭自动 safe area 后窗口内容仍从状态栏下方开始，
        // 但 IInsetsManager 仍上报一个远大于真实状态栏高度的 Top（疑为物理像素未换算 / 叠加 decor），
        // 原实现把它原样加进 AppBar.MinHeight + Padding.Top，工具行被推到底部，上方留出大片空白蓝。
        // 修复：AppBar 只保留 56dp 工具行，不再叠加 top inset；状态栏配色由 styles.xml 的
        // statusBarColor 负责（非 edge-to-edge 机型生效），真机上状态栏本就是独立的一行。
        // bottom 仍需钳制：异常上报会让底部操作栏离屏一大截。
        var bottom = Math.Clamp(inset.Bottom, 0, MaxVerticalInset);
        AppBar.MinHeight = 56;
        AppBar.Padding = new Thickness(inset.Left, 0, inset.Right, 0);
        // 内容区两侧/底部让出手势导航条（各页底部操作栏因此不被遮挡）
        PageHost.Margin = new Thickness(inset.Left, 0, inset.Right, bottom);
        Toast.Margin = new Thickness(inset.Left, 0, inset.Right, bottom + 24);
    }

    // ---- 左缘滑动开抽屉 ----

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is { DrawerOpen: false })
        {
            var pos = e.GetPosition(Root);
            if (pos.X <= EdgeSlop) _edgePress = pos;
        }
    }

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_edgePress is not { } start)
            return;
        if (_vm is not { } vm || vm.DrawerOpen)
        {
            _edgePress = null;
            return;
        }
        var pos = e.GetPosition(Root);
        var dx = pos.X - start.X;
        var dy = pos.Y - start.Y;
        if (dx > EdgeSwipeThreshold && Math.Abs(dx) > Math.Abs(dy))
        {
            _edgePress = null;
            vm.ToggleDrawerCommand.Execute(null);
        }
    }

    private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
        => _edgePress = null;

    private void OnOverlayTapped(object? sender, RoutedEventArgs e)
        => (DataContext as MainViewModel)?.CloseDrawerCommand.Execute(null);

    /// <summary>静态导航项点击：Tag 存页面索引，改 SelectedPage 即触发页面切换 + 抽屉收起。</summary>
    private void OnNavClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Button { Tag: string tag } || !int.TryParse(tag, out var page)) return;
        _vm.SelectedPage = page;
        // 点当前页时 SelectedPage 不变、OnSelectedPageChanged 不触发，抽屉仍需收起
        _vm.DrawerOpen = false;
    }
}
