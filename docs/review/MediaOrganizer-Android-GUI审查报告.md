# MediaOrganizer-Android-GUI审查报告

# MediaOrganizer Android GUI 审查报告

> 审查对象：`self-exiler/MediaOrganizer`（main 分支），Avalonia 12.1.1 + FluentTheme。
> 范围：`src/MediaOrganizer.Android/` 全部视图（MainView / WorkbenchView / SettingsView / ReportView / FailedFilesView）、`App.axaml(.cs)`、`MainActivity.cs`。
> 日期：2026-11-02

---

## 一、结论摘要

| 维度 | 评价 |
| --- | --- |
| Fluent 视觉一致性 | 有自造色板/圆角/字号体系，与 Fluent 主题资源脱钩；深色模式被硬编码关闭；缺少 Fluent Motion（无过渡动画） |
| 下拉框交互 | **9 处 ComboBox 均为桌面导向的 Popup 下拉**，打开时的焦点/BringIntoView 联动导致页面 ScrollViewer 突然滚动，需整体替换为"选择字段 + 对话框"模式 |
| 触屏可用性 | 多处触控热区 36dp（低于 48dp 建议值）、正文字号 10–12sp 偏小、无安全区适配 |
| 架构基础 | 良好：VM 与视图分离清晰、已有 `IConfirmDialog` 原生对话框先例，替换下拉方案可低侵入落地 |

---

## 二、下拉框滚动跳动问题（重点）

### 2.1 现象与根因

手机端点击 ComboBox，页面会突然滚动一下。这不是布局 bug，而是 Fluent ComboBox 的固有行为链：

1. ComboBox 打开 Popup 时，会将焦点移到**当前选中的 ComboBoxItem** 上；
2. 聚焦触发该项的 `BringIntoView` 请求，请求沿可视树向上冒泡，**页面级 ScrollViewer 也会响应**，于是整页滚动到让 ComboBox"完整可见"的位置；
3. 同时 Popup 需要重新测量/定位（Avalonia Android 上 Popup 是独立顶层表面），打开瞬间的布局 pass 会放大这种跳动感。

这类控件本质上是为桌面鼠标场景设计的：**下拉列表依附于控件位置、最大高度受限、选项行高偏小**，在手机上既容易触发滚动联动，也不符合 Android 用户"居中弹窗单选"的操作预期。

### 2.2 涉及位置（共 9 处）

| 文件 | 行号 | 用途 |
| --- | --- | --- |
| SettingsView.axaml | 40 | 最大年份回溯 |
| SettingsView.axaml | 46 | 未来日期缓冲 |
| SettingsView.axaml | 127 | 网络位置类型 |
| SettingsView.axaml | 183 | 扫描并行度 |
| SettingsView.axaml | 189 | 执行并行度 |
| WorkbenchView.axaml | 86 | 输出目标 |
| WorkbenchView.axaml | 140 | 分级目录 |
| WorkbenchView.axaml | 146 | 操作类型（内联 ComboBoxItem） |
| WorkbenchView.axaml | 154 | 同名处理 |

### 2.3 推荐方案：OptionPicker + 单选对话框

统一替换为"**只读选择字段（Button 样式）+ 点击弹出单选对话框**"。VM 完全不动（仍绑定 Options / SelectedIndex / SelectedValue），只换控件层。

**方案 A（推荐）：原生 **`AlertDialog.SetSingleChoiceItems`

项目已有 `AndroidConfirmDialog`（Platforms/AndroidConfirmDialog.cs）使用系统 AlertDialog 的先例，扩展一个 `IOptionDialog` 服务即可：

```csharp
public interface IOptionDialog
{
    Task<int> PickAsync(string title, IReadOnlyList<string> options, int selectedIndex);
}

// Android 实现要点：
var builder = new AlertDialog.Builder(MainActivity.Instance);
builder.SetTitle(title);
builder.SetSingleChoiceItems(options.ToArray(), selectedIndex,
    (_, e) => { tcs.TrySetResult(e.Which); dialog.Dismiss(); });
builder.SetNegativeButton("取消", (_, _) => tcs.TrySetResult(-1));
```

优点：

- 对话框是独立窗口，**与页面 ScrollViewer 完全解耦，从机制上根除滚动跳动**；
- 系统级大触控行（约 48–56dp）、单选 RadioButton、返回键/点外部取消全部免费获得；
- 与现有确认弹窗视觉一致，实现量最小。

配套做一个 Avalonia 侧 `OptionPicker` 控件（外观为显示"当前值 + › 箭头"的 Button，MinHeight 48），`Click` 时调用 `IOptionDialog.PickAsync` 后回写绑定源。视图替换示例：

```xml
<!-- 原： -->
<ComboBox ItemsSource="{Binding ScanParallelOptions}"
          SelectedValueBinding="{ReflectionBinding Value}"
          SelectedValue="{Binding MaxDegreeOfParallelism}"/>
<!-- 新： -->
<local:OptionPicker Title="扫描并行度"
                    Options="{Binding ScanParallelOptions}"
                    SelectedValue="{Binding MaxDegreeOfParallelism}"/>
```

**方案 B（备选）：Avalonia 自绘模态对话框**

若希望跨端（Desktop 复用同一 View）或对话框内样式完全可控，可在 MainView 根 Grid 加一层全屏遮罩 + 居中卡片（RadioButton 列表），复用现有抽屉遮罩的写法。代价是需要自己处理返回键拦截、焦点圈定与动画，工作量约为方案 A 的 2–3 倍。

**不建议的过渡方案**：给 ComboBox 设置 `MaxDropDownHeight`、保留 Popup 仅调样式——只能缓解观感，无法消除 BringIntoView 引起的页面滚动。

---

## 三、Fluent Design 不完善之处

### 3.1 主题与色彩

| # | 问题 | 证据 | 建议 |
| --- | --- | --- | --- |
| F1 | `RequestedThemeVariant="Light"` 硬编码，永不跟随系统深色 | App.axaml:5 | 改为 `Default`（跟随系统），或提供设置项 |
| F2 | 全套自造色刷（`#0067C0`/`#F5F5F5` 等硬编码 Hex 散布各视图），未使用 Fluent 主题资源键 | App.axaml:8–15；各视图大量内联 Hex | 收口为语义化资源并用 `DynamicResource`，否则深色适配需逐视图改几十处 |
| F3 | 按钮只有 `:pointerover` 态，没有 `:pressed`；触屏上 pointerover 基本不触发，**主按钮按下无视觉反馈** | App.axaml:52–54 | 补 `:pressed` 态（如 `#005A9E`），danger/sm 同样补齐 |
| F4 | 信息提示条颜色（`#F3F9FD`/`#C3DCF1`/`#0F4E7D` 等）在 4 个视图重复硬编码 | WorkbenchView:109/169、SettingsView:76/157 | 抽为 `InfoBarBrush` 系列资源，或直接实现 InfoBar 样式类 |

### 3.2 Fluent Motion 缺失

| # | 问题 | 证据 | 建议 |
| --- | --- | --- | --- |
| F5 | 抽屉仅靠 `IsVisible` 瞬间显隐，无滑入/淡入 | MainView.axaml:77–79 | 加 `TranslateTransform` 滑入（200ms，Fluent 减速曲线）+ 遮罩淡入 |
| F6 | 页面切换 ContentControl 直接替换内容，无过渡 | MainView.axaml:56 | 轻量淡入/位移动画；分析结果卡片出现同理（WorkbenchView:193） |

### 3.3 视觉规范细节

| # | 问题 | 证据 | 建议 |
| --- | --- | --- | --- |
| F7 | 圆角 token 不统一：卡片 12、按钮 8、chip 10、步骤圆 11、输入框 8 | 各视图 | 定义 `RadiusS=8 / RadiusM=12` 两级即可，chip 用全圆角 |
| F8 | 汉堡按钮用文本字符 `≡` 代替图标，视觉粗糙且依赖字体字形 | MainView.axaml:48 | 换 `PathIcon`（三条横线矢量） |
| F9 | 字号梯度偏小且碎片化：10/11/12/13/14 混用，状态栏与报告正文仅 11sp | ReportView.axaml:19、MainView.axaml:68–71 等 | 建立 Fluent 字阶：正文 14、辅助 12、说明性最小 12；报告正文建议 13–14 |
| F10 | 顶栏为实心 Accent 色块，与整体浅色卡片风格割裂 | MainView.axaml:44 | 可保留品牌色，但建议改浅色表面 + 深色标题的 Fluent 移动惯例，降低视觉重量 |

---

## 四、其他移动端交互问题

| # | 严重度 | 问题 | 证据 | 建议 |
| --- | --- | --- | --- | --- |
| M1 | 高 | **无安全区适配**：顶栏、底部状态栏、各页底部操作栏均未处理状态栏/手势导航条 insets，Android 15 强制 edge-to-edge 下会被遮挡 | MainView.axaml:44,59；WorkbenchView.axaml:263；ReportView.axaml:29；FailedFilesView.axaml:106 | 根 Grid 应用 SafeAreaPadding，或顶栏/底栏分别加系统 insets 边距 |
| M2 | 高 | 返回键只拦截抽屉；在设置/报告/失败文件页按返回**直接退出 App** | MainActivity.cs:67–78 | 非工作台页按返回先导航回工作台，再按一次退出 |
| M3 | 中 | `Button.sm` 及多处行内按钮 MinHeight=36，低于 48dp 触控建议 | App.axaml:56；WorkbenchView:75,96,120,197；FailedFilesView:93 | 行内操作按钮热区提到 44–48（视觉高度可不变，用 Padding 撑热区） |
| M4 | 中 | 网络位置行"编辑/删除/测试"三按钮并排 + 名称/地址，窄屏拥挤易误触（删除紧邻编辑） | SettingsView.axaml:106–111 | 改为整行点击进入编辑页，或行尾"⋯"弹出操作菜单（可复用方案 A 的对话框） |
| M5 | 中 | 报告正文为不可选中的 `TextBlock`，无法复制片段，且 11sp 阅读困难 | ReportView.axaml:19 | 换 `SelectableTextBlock`，字号 ≥13 |
| M6 | 中 | 权重 Slider 范围 0–2、步进 0.1，触屏上难以精确拖动 | SettingsView.axaml:24–26 | 改 −/+ 步进器，或复用 OptionPicker 提供 0/0.5/1.0/1.5/2.0 档位 |
| M7 | 低 | 硬编码 `MaxWidth`（230/170/180）在小屏或系统大字体下截断文本 | SettingsView:65,102；ReportView:17 | 改 Grid 星号列 + TextTrimming，不写死像素宽 |
| M8 | 低 | 设置项标签文字不可点击，只有 ToggleSwitch 本体可点 | SettingsView:18–22、169–175 | 整行 Tapped 切换，提升触达率 |
| M9 | 低 | 失败文件行内 CheckBox 触控区小 | FailedFilesView.axaml:57 | 行首 48dp 宽点击区，点行首即切换勾选 |

---

## 五、落地优先级建议

1. **P0 — 下拉框改造**（9 处 ComboBox → OptionPicker + AlertDialog 单选）：一次控件抽象，全部替换，VM 零改动。
2. **P0 — 安全区 insets**（M1）：Android 15+ 真机可见的遮挡问题。
3. **P1 — 返回键导航**（M2）、`:pressed`** 反馈**（F3）、**行内按钮热区**（M3）。
4. **P2 — 主题资源收口 + 深色模式**（F1/F2/F4）、抽屉与页面过渡动画（F5/F6）。
5. **P3 — 字阶/圆角 token 统一、报告可复制、Slider 改步进**等体验打磨。

> 建议按项目惯例走 grill-me 需求评审 → 补充 SRS-Android 修订 → 实施的流程；P0 两项可作为独立批次先行。
