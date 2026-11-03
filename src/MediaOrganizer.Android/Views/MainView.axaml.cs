using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MediaOrganizer.Android.ViewModels;

namespace MediaOrganizer.Android.Views;

/// <summary>
/// 应用外壳（FR-A7）：AppBar + 4 个常驻页面 + 抽屉（滑入动画/左缘手势/点遮罩关闭）+ 操作反馈轻提示。
/// 安全区：接管 Avalonia 的自动 padding——AppBar 向上长出状态栏 insets（品牌色带延伸到状态栏背后），
/// 内容区与 Toast 在底部让出手势导航条（审查报告2 M1：Android 15+ 强制 edge-to-edge 下不再被遮挡）。
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>左缘手势起触点：按点距左边缘 ≤24px 才记录（审查报告3 抽屉手势）。</summary>
    private const double EdgeSlop = 24;
    /// <summary>触发开抽屉的水平位移阈值。</summary>
    private const double EdgeSwipeThreshold = 60;

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
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedPage) && _vm is { } vm)
            UpdatePages(vm.SelectedPage);
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
        // 关闭自动 padding（作用于根，会让 AppBar 色带上端露出窗口底色），改为手动接管
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
        // AppBar 色带向上延伸状态栏高度，内容经 Padding 下沉；深色状态栏图标区域同样是品牌色
        AppBar.MinHeight = 56 + inset.Top;
        AppBar.Padding = new Thickness(inset.Left, inset.Top, inset.Right, 0);
        // 内容区两侧/底部让出手势导航条（各页底部操作栏因此不被遮挡）
        PageHost.Margin = new Thickness(inset.Left, 0, inset.Right, inset.Bottom);
        Toast.Margin = new Thickness(inset.Left, 0, inset.Right, inset.Bottom + 24);
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
}
