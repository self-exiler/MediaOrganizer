# 踩坑记录：Avalonia 安卓端中文缺字——从「系统字体回退」到「自打包字体」的完整复盘

- 日期：2026-10-03（2026-10-04 合并原《2026-09-05-系统字体与并行度评估.md》：字体结论并入本文，并行度评估作为独立章节见 §七）
- 项目：MediaOrganizer（github.com/self-exiler/MediaOrganizer）
- 环境：Avalonia 12.1.1（Avalonia.Android / Avalonia.HarfBuzz 12.1.1）、.NET 10、android-arm64、APK
- 测试机：荣耀 Magic5（PGT-AN10，MagicOS / Android 16 / API 36）

## TL;DR

1. 这个坑有两层独立成因：一层是**应用代码 bug**（`FontFamily` 标记扩展陷阱，已修复），另一层是**框架 × ROM 兼容缺陷**（Skia 系统字体回退在 MagicOS 上真机不可用，非开发者能在应用侧解决）。
2. **必须自打包字体**：把不可靠的外部依赖（系统字体回退）替换成确定性的内部资产，是当前机型组合下唯一稳妥的解法；代价是 APK 增加约 7MB（实测，见 §五）。**Avalonia 12 技术栈目前处于开发初期，对国产定制手机 OS 的字体支持存在问题**，系统字体的两条路径均已真机否证，自打包是代价最小的方案。
3. 过程中出现过一次**误诊**：把代码 bug 的锅扣在"框架系统字体回退缺陷"上，导致 09-06 移除内置字体、v1.1.0 真机中文再次不可见，09-07 被迫恢复。**核心教训：注释里的"结论"必须可验证，未经真机验证的定论会引发连锁误决策。**

## 一、事故时间线

| 日期 | 提交 | 事件 |
|---|---|---|
| 08-25 | `5629327e` | 初版安卓代码：首次内置 NotoSansSC-Regular.otf（8,331,336 B ≈ 7.94MB），同时埋下 bug——`ContentControlThemeFontFamily` 写成 `<FontFamily>{StaticResource AppFont}</FontFamily>` |
| 08-29 | `d1104aa` | 修复中文显示（改为 `avares://…#Noto Sans SC` URI 字面量），中文恢复；注释留下**误诊结论**："Avalonia 12 Android 系统字体回退缺陷致 CJK 空白" |
| 09-05 | 评估文档 | 翻案——认定真凶是标记扩展 bug；经反编译确认回退路径代码存在，理论上系统字体可行；制定「两步切换、每步可回退」方案（该文档已于 10-04 并入本文） |
| 09-06 | `50aaed0` | 实施：移除 App.axaml 全部字体覆盖、删除 csproj 的 AvaloniaResource 行、删除 Fonts/ 目录，改用系统字体 |
| 09-07 | `9c4d4e8` | v1.1.0 真机验证：中文再次不可见 → **恢复内置字体**（git 还原 App.axaml 与 Fonts/），注释固化"不要再移除本字体" |
| 09-07 | `0f44e02` | 小插曲：csproj 注释里写了 `git checkout HEAD~ -- file`，双连字符违反 XML 注释规则触发 MSB4025 —— 注释里的命令也要合法 |
| 09-25 | 分支实验 | 方案 B：不打包字体，改在组合根给 `FontManagerOptions.FontFallbacks` 显式列出系统 CJK 族名——荣耀真机**仍中文不可见**，系统字体第二条路径同样被否证 |

**结论定格在两件事**：① 标记扩展陷阱是代码 bug，必须修；② 即便代码全对、移除字体走系统回退，这台机器上中文仍不可见——**系统字体回退路径在本应用（Avalonia 12.1.1 / Android）上确实不可用**，两条路径均已真机否证。

## 二、根因拆解：两件独立的事

### 根因一（应用代码层面，可修）：FontFamily 标记扩展陷阱

```xml
<!-- 错误写法：元素内容不展开标记扩展，值变成字面字符串 "{StaticResource AppFont}" -->
<!-- Avalonia 中 FontFamily 的元素内容按"字体族名字符串"解析 -->
<FontFamily x:Key="ContentControlThemeFontFamily">{StaticResource AppFont}</FontFamily>

<!-- 正确写法：直接写 avares:// URI 字面量 -->
<FontFamily x:Key="ContentControlThemeFontFamily">avares://MediaOrganizer.Android/Fonts/NotoSansSC-Regular.otf#Noto Sans SC</FontFamily>
```

危害链（反编译 Avalonia.Skia 12.1.1 证实）：

- `FontManagerImpl.TryMatchCharacter` 会把族名**原样透传**给 `SKFontManager.MatchCharacter(familyName, …)`（L121）；
- 非法族名（`{StaticResource AppFont}`）使**按码点的系统字体回退一并失效**；
- 表现：依赖 `ContentControlThemeFontFamily` 主题键的控件（Button/ComboBox/ContentControl 等模板内控件）中文全部不可见，**只有 ASCII/符号可见**；普通 TextBlock 不一定受影响——这就是"看不到汉字但英文正常"的典型特征。

### 根因二（框架 × ROM 层面，真机验证存在）：Skia 系统回退链在 MagicOS 上不可用

Avalonia 在 Android 上**不走系统原生文本管线的 fallback**（原生 App 的 Minikin/HWUI 那套），而是由 Skia 的字体管理器（`SKFontManager.Default`，读 `/system/etc/fonts.xml` + `/system/fonts`）自行做按码点回退（`TryMatchCharacter → MatchCharacter`）。

- **理论链路**：默认字体（sans-serif → Roboto）缺 CJK 字形时，`MatchCharacter` 应命中系统 CJK 字体（荣耀：HONOR Sans / HarmonyOS Sans；原生：Noto Sans CJK SC；小米：MiSans）——反编译确认这条路径**代码上存在**。
- **真机现实（两条路径均否证）**：

  | 时间 | 尝试路径 | 机制 | 结果 |
  |---|---|---|---|
  | 2026-09-06/07 | 方案 A：纯默认族 | Skia 内部按码点查 ROM fallback 表（`matchFamilyStyleCharacter` → `/system/etc/fonts.xml`） | 荣耀真机**中文不可见**，v1.1.0 发布事故 |
  | 2026-09-25 | 方案 B：显式族回退 | Avalonia `FontManager.TryMatchCharacter` 首查 `_fontFallbacks`，按具名族 `SKFontManager.MatchFamily` 解析 | 同机**仍中文不可见**（抽屉导航项、按钮全空白） |

- **疑似机理**：MagicOS 的 CJK 字体是**可变字体（variable font）**——HONOR Sans / HarmonyOS Sans 是"无级可变"（字重、中宫均可变）。Skia 的 Android 字体管理器对可变字体族按族名/码点的匹配存在兼容问题：`MatchCharacter` 可能返回空、按错误的可变实例命中，或命中覆盖不全的字体 → 出现豆腐块/空白/部分汉字缺失。
- **旁证**：同引擎在 Flutter 端也出现过 Android 字体回退顺序重构后的 CJK 渲染回归事故（背景链接见附录 C）；原生 Android 应用走系统文本堆栈，无此问题——这也是"非开发者能在应用侧解决系统回退"这一判断的依据。
- **判断**：Avalonia 12 技术栈处于开发初期，对国产定制手机 OS（荣耀 MagicOS 等）的字体支持存在问题，属框架 × ROM 的兼容性缺陷，**应用侧无法修复**。

## 三、为什么必须先自打包

- Avalonia（Skia 文本栈）的系统回退可靠性完全取决于 ROM 的字体打包方式与 Skia 版本的组合，**开发者无法从应用侧修复**（改不了 Skia，也改不了 ROM 的 fonts.xml）；
- 自打包字体 = 把**不可靠的外部依赖替换为确定性内部资产**：不依赖任何 ROM 的字体环境、跨机型渲染一致；
- 是跨平台框架处理 CJK 的**标准工程手段**（GitHub 上 Avalonia 中文相关 issue 的主流结论），不是绕过 bug 的妥协；
- **代价最小**：仅少量增加包体积（实测约 7MB），相比因中文缺失导致的返工与发布事故，这是当前机型组合下代价最小的选择。

## 四、最终方案与配置要点

当前配置（已真机验证有效，分三层）：

```xml
<!-- ① csproj：AvaloniaResource 打包。必须放 Fonts/ 而非 Assets/，
      否则 .NET Android 会把 Assets/** 自动标记为 AndroidAsset，与 AvaloniaResource 冲突 -->
<ItemGroup>
  <AvaloniaResource Include="Fonts\**" />
</ItemGroup>
```

```xml
<!-- ② App.axaml：资源定义 + 主题键覆盖。注意写法全部是 URI 字面量 -->
<FontFamily x:Key="AppFont">avares://MediaOrganizer.Android/Fonts/NotoSansSC-Regular.otf#Noto Sans SC</FontFamily>
<!-- 覆盖 FluentTheme 模板内部引用的主题字体键，否则 Button/ComboBox 等回到默认族而缺字 -->
<FontFamily x:Key="ContentControlThemeFontFamily">avares://MediaOrganizer.Android/Fonts/NotoSansSC-Regular.otf#Noto Sans SC</FontFamily>
```

```xml
<!-- ③ 全局字体兜底：TextElement.FontFamily 可继承，作用于所有控件（含 PopupRoot 弹层）；
      TextBlock/AccessText 显式命中，避免 ContentPresenter 生成文本框时漏继承。
      ⚠️ Selector 必须写附加属性形式 TextElement.FontFamily：裸写 FontFamily 用在 Control 上会触发 AVLN2000 -->
<Style Selector="Control">
  <Setter Property="TextElement.FontFamily" Value="{StaticResource AppFont}"/>
</Style>
<Style Selector="TextBlock">
  <Setter Property="TextElement.FontFamily" Value="{StaticResource AppFont}"/>
</Style>
<Style Selector="AccessText">
  <Setter Property="TextElement.FontFamily" Value="{StaticResource AppFont}"/>
</Style>
```

注意事项（写进代码注释的纪律）：

1. **不要还原成元素内容写 `{StaticResource}`**——这是本次最大的坑；
2. Noto Sans SC 不含 emoji 与生僻扩展字，UI 文本与测试用例避免使用；
3. **不要为省体积做子集化**：本应用要渲染用户文件名（可能含生僻字），而系统回退链在目标机型已证伪，子集化必然出豆腐块；
4. 备选路径 `FontManagerOptions.FontFallbacks`（显式列出系统 CJK 族名，如 HONOR Sans）**已试（09-25）并失败**，属"命名族加载"而非"按码点回退"，在同一机型上同样不可用——不再尝试。

## 五、代价与取舍

| 维度 | 影响 |
|---|---|
| APK 体积 | 内置 OTF 8,331,336 B ≈ 7.94MB；以 deflate 存储于 APK 的 `libassembly-store.so`（压缩率约 89%），**实测增约 7MB**（满足 NFR-A3 60MB 上限） |
| emoji | 系统字体本可回退到系统 emoji 字体；自打包 Noto Sans SC 不含 emoji → UI 需避开 emoji |
| 跨设备一致性 | 弱点是牺牲，换来强项：不再受国产 ROM 字体差异影响，渲染完全确定 |
| 升级复评 | Skia 对可变字体支持逐年进步，Avalonia 大版本升级后应重跑系统字体实验再决定去留（见附录 B） |

## 六、经验教训（本次复盘的精华）

1. **注释里的"结论"必须可验证**。误诊结论在代码里躺了近两周，直到 09-05 评估才被翻案；若当时就做真机对照实验，本可省掉 v1.1.0 一次发布事故。写结论时附上验证状态（已验证 / 待验证 / 怀疑），别把猜测写成事实。
2. **理论可行 ≠ 真机可行**。反编译证明 `MatchCharacter` 回退路径代码存在（"路径存在"），不等于设备上能命中（"链路可用"）。跨平台框架的字体回退不等于系统级回退，Android 必须真机验收。
3. **回退方案要可回滚**。评估预埋的两步切换（先验渲染、后删体积）和 git 还原命令，让翻车恢复只花了一次提交——验证过程本身也要按"可逆"设计。
4. **排查顺序：先查自己的代码，再怀疑框架**。本次第一层根因是纯代码 bug；对框架的怀疑要有反编译/源码证据支撑，不要靠"印象"定罪。
5. **国产 ROM 是差异化测试对象**。荣耀（HONOR Sans）、小米（MiSans）、原生（Noto Sans CJK SC）字体环境各不相同；本次只在荣耀 PGT-AN10 上验证，将来换机应重跑验证清单（全页面中文 / Button/ComboBox / 弹窗 Popup / 用户文件名含生僻字与 emoji / 深浅主题）。
6. **设备特有调试成本**：荣耀机型加密 logcat，需自建现场日志（本项目已用 CrashLogger 前置落盘）——侧面印证真机调试比想象中贵，更要把验证清单一次做全。

## 七、扫描/执行并行度评估

> 本章为 2026-09-05 评估文档保留内容，与字体结论相互独立。
> 结论：静态审查未发现「非自动档值本身」的不安全路径；「非自动时闪退」待真机取证。

### 7.1 值域与守卫（全部消费点逐一核对）

| 环节 | 事实 |
|---|---|
| UI 值域 | 安卓 ComboBox 白名单 `[0,1,2,4]`（执行）/ `[0,1,2,4,8]`（扫描），索引→值映射，越界回退 0；桌面 NumericUpDown 0..64 |
| 配置回读 | 未知历史值经 `IndexOf` <0 → 0（自动），不会带出非法值 |
| Analyzer（扫描） | ctor `≤0 → Environment.ProcessorCount`；`Parallel.ForEachAsync` 的 ParallelOptions 合法 |
| OrganizeSession（执行） | `>0 ? 配置值 : (网络4/本地2)` |
| FileOperator | ctor `≤0 → 2` 归一 |
| 线程安全 | 提取器无状态；`AndroidExifReader` 每调用独立 `ExifInterface`；`PatternEngine` 只读静态 + `ConcurrentDictionary` 正则缓存；TagLib 每文件独立实例；FileOperator/SmbFileStorage 自有并发控制 |

**静态结论：1/2/4/8 这些值本身不会构造出崩溃路径；设置动作本身也只是 int 赋值 + SaveConfig（与其它设置项同路径）。**

### 7.2 测试者「非自动时闪退」的可能真因（按嫌疑排序，待真机取证）

1. **视频提取的内存峰值 × 并行度联动**：EXIF 提取器对 ≤200MB 的视频走「整文件读入 MemoryStream」做 TagLib 解析（`ExifExtractor.MaxVideoBytesForTagRead`）。DOP=8 并发命中多个大视频时内存峰值可达 GB 级 → Android 直接杀进程（表现即闪退，无 .NET 异常可捕获）。4 核机上 auto=4 而显式选 8 时，显式值**确实**比 auto 更危险——与「非自动才出事」的报告吻合；
2. **本地目标拷贝缓冲**：LocalFileStorage 每路副本租 8MB 缓冲，DOP=8 → 64MB 峰值（次要）；
3. 报告不精确：测试者版本、操作序列（设置后是分析崩还是执行崩）未知。

### 7.3 取证与加固建议

- 取证（换机测试时）：`adb logcat -s Mono,DotNet,AndroidRuntime *:E` 下复现「设置非自动 → 分析/执行」，分别取 1/2/4/8 对比；崩溃时保留完整堆栈；
- 可选加固（如证实为假设 1）：给视频提取加「并发内存预算」（同时在读的 TagLib 视频总字节数 ≤ 400MB，超出的排队），并让 LocalFileStorage 缓冲随 DOP 缩放。

## 八、附录

### A. 设备端取证代码（复现/确认回退是否命中的最小探针）

```csharp
// 在目标设备上跑一次，判断"系统 CJK 字体对 Skia 是否可见"
using SkiaSharp;

// 1. 按码点匹配：'汉' 应该命中某系统中文字体；返回 null = 回退链断裂的实证
var tf = SKFontManager.Default.MatchCharacter('汉');
if (tf == null)
    Console.WriteLine("[探针] MatchCharacter('汉') = null → 系统 CJK 对 Skia 不可见");

// 2. 枚举可见族名：确认 HONOR Sans / HarmonyOS Sans 是否被 Skia 枚举到
foreach (var f in SKFontManager.Default.FontFamilies)
{
    if (f.Contains("Sans") || f.Contains("HONOR") || f.Contains("Harmony"))
        Console.WriteLine($"[探针] family: {f}");
}
```

### B. 相关提交与索引

- 本记录对应修复提交：`d1104aa`（修复中文显示）、`9c4d4e8`（恢复自打包字体）、`50aaed0`（移除字体实验，勿再尝试）
- 复评触发条件：Avalonia/Skia 大版本升级后，重跑"移除字体 → 真机验证"两步实验，通过则可摘除字体包

### C. 背景旁证链接

- Flutter/Skia 在 Android 端字体回退顺序重构后出现 CJK 渲染问题的拆解（同引擎旁证）：https://zhuanlan.zhihu.com/p/2038181420749813325
- Noto Sans SC 为 SIL OFL 许可，可随 APK 分发，无再分发限制。