
安卓 GUI 审查报告（Avalonia 12.1.1 / FluentTheme）
总评：整体骨架健康——48dp 热区、卡片化布局、返回键拦截、虚拟化失败列表、字体兜底都已做到位。主要问题集中在三块：FluentTheme 只被当作控件模板库使用，其主题资源体系（强调色/深浅主题）基本没接入；9 处桌面式 ComboBox 不适配触屏；以及若干交互安全与状态保持的细节。

一、Fluent Design 不完善之处
F1. Fluent 控件强调色与品牌色脱节（P0，修复成本极低）
App.axaml 定义了品牌色 AccentBrush #0067C0，但没有设置 SystemAccentColor。FluentTheme 内部控件——ToggleSwitch 开启态、Slider 轨道、CheckBox 勾选、ProgressBar 填充、TextBox 聚焦下划线、ComboBox 选中高亮——全部走框架默认强调色（取值依 ROM/Avalonia 而定），与品牌蓝不一致。这是“自定义画刷 + FluentTheme”双轨并行导致的必然结果。

修法：在 Application.Resources 覆盖 SystemAccentColor（及 Light1-3/Dark1-3 变体），一行资源即可让全部 Fluent 控件归位到 #0067C0。

F2. 无深色主题（P1，工作量中大）
App.axaml#L5 硬编码 RequestedThemeVariant="Light"，且全部颜色为散落各处的硬编码 hex（视图里还有 #616161、#F8F8F8 等未收口进资源键的）。FluentTheme 本身支持 Light/Dark 切换，当前只用了它的控件模板，没有用 ThemeDictionaries。Android 10+ 系统深色模式下本应用是刺眼的全白屏。另外 SettingsViewModel.cs#L93 的 ThemeOptions（跟随系统/浅色/深色）在安卓端完全没接。

建议：v1.0 冻结期可不做（ADR-0008），但应把颜色统一迁入 ThemeDictionaries（Light/Dark 两套），这是后续一切主题化的地基；否则每个视图里的裸 hex 都是未来的迁移债。

F3. 主按钮无按压反馈（P1）
App.axaml#L45-L54 的 Button.primary 只定义了 :pointerover——触屏上没有悬停，等于零反馈；且该样式声明在 FluentTheme 之后，把 Fluent 自带的 :pressed 背景也覆盖掉了。Button.danger（WorkbenchView.axaml#L33-L38）同样问题。按 Fluent 规范触屏必须有 pressed 态。补 Button.primary:pressed（如 #005A9E）与 :disabled（不透明度）即可。

F4. ComboBox 弹出列表项是桌面尺寸（P0，随下拉方案一并解决）
App.axaml.cs#L159-L166 的 AppTheme 只把 ComboBox 本体抬到 44dp，弹出的 ComboBoxItem 仍是 FluentTheme 默认的 ~22px 桌面高度，远低于 48dp 触控标准，弹层外观（阴影/边框）也是桌面样式。若采用下述对话框方案，此项自然消失。

F5. 图标依赖字体字形，跨 ROM 不可控（P1）
汉堡菜单 ≡、▶ 开始分析、↻ 重新分析、× 取消、›、⚠、↗、✓ 全部是文本字形。项目自己的字体踩坑记录已证明 ROM 字体环境不可预测——NotoSansSC 不含的字符回退系统字体，各机型渲染不一。Fluent 的标准做法是 Path/StreamGeometry 矢量图标。另外原型中抽屉导航项有图标（原型 L190-202），MainView.axaml#L107-L118 实现里全部缺失，导航辨识度下降。

F6. 系统状态栏颜色未与 AppBar 同步（P2）
styles.xml 的 MyTheme 只设了 windowBackground，没设 android:statusBarColor。AppBar 是 #0067C0，系统状态栏是 AppCompat 默认色，顶部会出现一条色差带。同时原型里 AppBar 带投影（原型 L25），实现是纯平 Border——建议补 statusBarColor=#0067C0 + AppBar 轻投影。

二、下拉选项框：问题定位与替代方案
现象与根因
9 处 ComboBox 全部嵌在页面级 ScrollViewer 内。点击时页面突然滚动一下，是 FluentTheme ComboBox（桌面控件）在移动端的固有摩擦：点击使 ComboBox 获得焦点，ScrollContentPresenter 会对获得焦点的子元素做一次 BringIntoView 偏移修正；同时弹出层打开时的定位/尺寸测量会引起一次布局往返——在 Avalonia Android 的单窗口渲染下被放大为肉眼可见的页面跳动。此外弹出层还有 F4 的触控尺寸问题。值得注意的是，原型用的是 HTML <select></select>（原型 L237-242），在真机上本就是系统级选择对话框——所以“点开弹系统选择器”才符合原设计意图，ComboBox 反而是实现走样。

受影响清单（9 处）
页面	字段	绑定方式	位置
工作台	输出目标	SelectedIndex	WorkbenchView#L86-88
工作台	分级目录	SelectedIndex	WorkbenchView#L140-142
工作台	操作类型（含禁用项）	SelectedIndex	WorkbenchView#L146-150
工作台	同名处理	SelectedIndex	WorkbenchView#L154-156
设置	最大年份回溯	SelectedValue (IntOption)	SettingsView#L39-43
设置	未来日期缓冲	SelectedValue	SettingsView#L45-49
设置	网络类型（编辑表单）	SelectedIndex	SettingsView#L126-129
设置	扫描并行度	SelectedValue	SettingsView#L182-186
设置	执行并行度	SelectedValue	SettingsView#L188-192
方案对比
方案 A：原生 AlertDialog 单选（推荐）	方案 B：Avalonia 自绘底部弹层
滚动跳变	根除（无 Avalonia 弹层参与）	根除
触控尺寸/涟漪反馈	系统级，天然达标	需自己实现
与既有架构契合	复用 AndroidConfirmDialog 同款模式	需在 MainView 加全局遮罩层 + 返回键拦截
Fluent 风格统一性	系统对话框样式	完全可控、可做滑入动画
工作量/风险	小（v1.0 冻结期友好）	中偏大
推荐方案 A，与项目现有 IConfirmDialog 注入模式完全同构：

C#

// Shared/Services：与 IConfirmDialog 并列
public interface IOptionPicker
{
    /// 






    Task<int></int> PickAsync(string title, IReadOnlyList<string></string> options, int selectedIndex);
}

C#

// Android/Platforms/AndroidOptionPicker.cs：与 AndroidConfirmDialog 同模式
public sealed class AndroidOptionPicker : IOptionPicker
{
    public Task<int></int> PickAsync(string title, IReadOnlyList<string></string> options, int selectedIndex)
    {
        var activity = MainActivity.Instance;
        if (activity is null) return Task.FromResult(-1);

        var tcs = new TaskCompletionSource<int></int>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            var dialog = new AlertDialog.Builder(activity).Create();
            dialog.SetTitle(title);
            dialog.SetSingleChoiceItems(options.ToArray(), selectedIndex,
                (d, which) => { tcs.TrySetResult(which); d.Dismiss(); });
            dialog.SetButton((int)DialogButtonType.Negative, "取消",
                (_, _) => tcs.TrySetResult(-1));
            dialog.SetOnCancelListener(new CancelListener(() => tcs.TrySetResult(-1)));
            dialog.Show();
        });
        return tcs.Task;
    }
}
触发控件外观：做一个 PickerField（48dp、外观对齐现有 input-box 样式、右侧 ▾ chevron、显示当前值），点击调用 picker。它暴露与 ComboBox 相同的 SelectedIndex / SelectedValue + SelectedValueBinding API，9 处替换即为机械改写，VM 层的 SelectedIndex/SelectedValue 属性与写回配置的逻辑完全不动。

两个特例：

“操作类型”的禁用项：网络目标时直接从选项列表中滤掉“移动”（已有的信息条负责解释原因），避免原生单选列表不支持逐项禁用的问题；
IntOption 的 SelectedValue 场景：PickerField 内部按 SelectedValueBinding 反查索引，选完回写值，与现在 ComboBox 语义一致。
三、其他 GUI 改进点
交互安全（P0）
删除网络位置无任何确认：SettingsView.axaml.cs#L36-L40 点击“删除”直接执行 SettingsViewModel.cs#L308，误触即删且不可恢复。项目已有 AndroidConfirmDialog，加一行确认即可。
导航与状态保持（P1）
页面切换丢状态：MainView.axaml#L56 的 ContentControl + ViewLocator 每次切换都 Activator.CreateInstance 重建视图——工作台/设置的滚动位置、失败文件展开详情、设置页网络编辑表单全部被重置。建议 MainView 缓存 4 个视图实例（VM 本来就是单例），或改为 4 个 ContentControl + IsVisible 切换。
抽屉无动画、无手势：MainView.axaml#L76-L79 用 IsVisible 硬切；原型有 0.25s 滑入过渡，Android 用户也习惯边缘滑动开抽屉。返回键拦截已做（好）。
触控规范（P1）
Button.sm MinHeight 36（App.axaml#L55-L59）低于 44/48dp，影响网络位置行的“编辑/删除/测试”（SettingsView#L106-L111）——三个 36dp 小按钮挤一行，建议改为“整行点击进编辑 + 行内仅留删除（带确认）”。
权重 Slider MinHeight 36（SettingsView#L24-26），thumb 命中区偏小，建议行高抬到 48。
工作台（P2）
统计摘要 4 列挤一行（WorkbenchView#L201）：390px 宽下每卡仅 ~80px，虽有 Viewbox 兜底仍显拥挤；原型是 2×2 网格（原型 L315-320），建议回归原型。
ProgressBar Maximum={Binding TotalFiles}（WorkbenchView#L245-246）：空源目录分析后 TotalFiles=0 → Maximum=0，存在除零/NaN 渲染风险，建议 Math.Max(TotalFiles,1) 或默认 100。
设置（P2）
软键盘：未设置 WindowSoftInputMode（MainActivity#L42-49），设置页底部密码框弹键盘时默认 pan 可能遮挡输入框，建议 adjustResize。
MaxWidth 硬编码（230/170/220，SettingsView#L65、SettingsView#L102、FailedFilesView#L68）：不同屏宽下截断表现不一，建议改用 * 列宽约束 + TextTrimming。
失败文件 / 报告（P2）
“全选”按钮无“取消全选”文案切换（FailedFilesView#L110），已全选时语义反转不明显。
报告页可补“复制文本”按钮（次要）。
四、修复优先级建议
优先级	事项	工作量
P0	ComboBox → 原生单选对话框（9 处，含 PickerField 控件）	中
P0	设置 SystemAccentColor 统一强调色	极小
P0	删除网络位置加确认	极小
P1	primary/danger 按钮补 pressed 态	小
P1	页面视图缓存（保滚动/表单状态）	小
P1	矢量图标 + 抽屉项图标 + 状态栏颜色	中
P1	触控尺寸整改（sm 按钮/Slider/网络行布局）	小
P2	抽屉动画、统计 2×2、ProgressBar 保护、软键盘、MaxWidth、全选文案	小
路线图	深色主题（ThemeDictionaries 迁移）	大
