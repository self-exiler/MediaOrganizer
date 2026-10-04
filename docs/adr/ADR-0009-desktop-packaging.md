# ADR-0009: 桌面版分发与打包策略（Native AOT + MSI）

- 状态：已接受（Accepted）——重写版，取代 2026-09-01 的"运行时外置"决策
- 日期：2026-09-05
- 修订：2026-10-02 决策 2 改选 NativeAOT（见文末「修订记录」），原"NativeAOT 不可控"的前提经实测推翻
- 修订：2026-10-04 决策 4/5 的实现由 Inno Setup 改为 WiX v5 出 MSI，语言包与 `.iss` 一并删除
- 关联：ADR-0006（Core/Shared/Desktop 分层）、ADR-0008（v1.0 范围冻结）
- 关联文件：`packaging/publish.ps1`、`packaging/build-installer.ps1`、`packaging/Product.wxs`、`packaging/README.md`

## 背景

桌面版进入可交付阶段，需要固化为可分发的安装程序。历史上有过两轮实测结论，仍是本次决策的有效输入：

### 1. 原生调试符号污染发布目录（2026-09-01 实测）

首轮按 FolderProfile 发布，产物 174 MB / 49 个文件，其中 59% 是终端用户用不到的原生符号：

| 冗余项 | 体积 |
| --- | --- |
| `libSkiaSharp.pdb` | 82 MB |
| `libHarfBuzzSharp.pdb` | 20 MB |
| `Magick.Native-Q16-x64.dll`（有用的原生库，非符号，列出仅供对比） | 23 MB |

根因：`SkiaSharp.NativeAssets.Win32` 与 `HarfBuzzSharp.NativeAssets.Win32` 把 `libSkiaSharp.pdb` / `libHarfBuzzSharp.pdb` 放在 `runtimes/win-x64/native/` 下，SDK 作为原生运行时资产原样拷贝——**不受 `DebugType` 控制**，改 `DebugType=none` 无效，只能在 Publish 后清理。

### 2. Avalonia 只依赖 `Microsoft.NETCore.App`

发布产物的 `runtimeconfig.json` 只声明 `Microsoft.NETCore.App`，**不含 `Microsoft.WindowsDesktop.App`**（Avalonia 不是 WPF/WinForms）。

### 3. 需求变更（2026-09-05）

用户明确要求：安装程序**运行时自包含**、**默认安装到用户目录**、启用 **AOT 或 ReadyToRun**。原"运行时外置"决策（框架依赖 + Velopack 引导在线补运行时）与"自包含"直接冲突，故整体重写本 ADR。

## 决策

### 1. 分发形态：自包含（内嵌 .NET 10 Runtime），取代原"运行时外置"

`--self-contained true`。理由：

- 目标机**无需预装任何 .NET 组件，离线可装**。原方案依赖安装时联网引导下载运行时，离线机器直接失败，这是用户明确不接受的场景。
- 免去安装器内的运行时检测 / 下载 / 静默安装逻辑，安装脚本大幅简化、失败面收窄。
- 代价是安装包 +约 30 MB（运行时经 LZMA2 压缩后），实测安装包约 47 MB，对桌面应用属可接受量级。

### 2. 预编译：NativeAOT（2026-10-02 修订，取代原"ReadyToRun，不采用 NativeAOT"）

选 **`PublishAot=true`**：

- 实测收益：发布目录 149.5 MB → 67.4 MB，冷启动（进程启动到窗口可见）1254 ms → 561 ms。
- 原否决理由不成立：本项目反射面只有两处——`ViewLocator` 的动态类型解析与 System.Text.Json 的反射式序列化。改造为静态 VM→View 登记表（`Desktop/ViewLocator.cs`）与源生成上下文（`Core/MediaOrganizerJsonContext.cs` + `AppJson`）后，项目代码 0 条 IL2026/IL3050，6 个导航页全部正常渲染。Avalonia 12 自带官方 AOT 发布路径，XAML 编译绑定本就无运行时反射。
- 防回归：Core / Shared 开 `<IsAotCompatible>`，普通构建常驻 AOT 分析器，新引入的反射会在编译期报警而非线上崩溃。
- 残留风险（发版前置检查项）：TagLibSharp 2.3.0、SMBLibrary、WebDAVClient 为无标注 netstandard2.0 程序集，报 `IL2104`——分析器看不见其内部。其中 TagLibSharp 用 `Assembly.GetTypes()` 建 mimetype→类型表，最可能在裁剪后静默失效（视频日期提取）。每次切 AOT 发版前须跑真实照片库回归。
- 代价：交叉产物不可回退到 R2R 通道（`publish.ps1` 现只保留 AOT 一条路径），出问题需回退提交重发。

### 3. 不启用独立的 PublishTrimmed

Native AOT 隐含**全量裁剪**，因此"裁剪友好"已是硬约束（见决策 2）。但 `PublishTrimmed` 作为 R2R 通道上的独立开关仍不启用：在没有 AOT 编译器的路径上做裁剪，只得到裁剪风险而没有本机代码收益。体积治理走 AOT + 剔除无用文件路线。

### 4. 打包工具：WiX v5（MSI），弃用 Velopack 与 Inno Setup（2026-10-04 修订）

主路线为 **WiX v5**（`dotnet tool install --global wix --version 5.0.2` + `WixToolset.UI.wixext/5.0.2`），产标准 MSI。取代原先的 Inno Setup 6.3。

- 自包含后不再需要 Velopack 的 `--framework` 运行时引导——那是选它的核心理由，理由已消失。弃用同时意味着**放弃自动更新通道**；v1.0 范围内可接受。
- **换掉 Inno 的直接动因**：`.iss` 手工罗列发布文件，Native AOT 后虽收敛到 5 个文件，但依赖一变目录内容就变，`.iss` 不会报错只会静默漏装；且 Inno 的卸载与 MSI 的 Upgrade 表互不相识，后续若再换容器无法平滑升级。WiX 侧由 `build-installer.ps1` 按发布目录实际内容生成清单，多文件/少文件都会被构建拦下。
- 中文向导由 `wix build -culture zh-CN` 直接选出（WiX UI 扩展自带 zh-CN 资源），**不再需要随仓库分发语言包文件**，故删除 `packaging/Languages/ChineseSimplified.isl`。
- 压缩由 `<MediaTemplate EmbedCab="yes" CompressionLevel="high" />` 承担，等价 Inno 的 LZMA2/ultra64。
- 代价：`.wxs` 是 XML，比 `.iss` 啰嗦，且 MSI 强制走 Windows Installer 的组件/产品码规则（`Product.wxs` 里的 GUID 一旦定了就永不可改，否则升级时旧文件不会被清理）。换来的是标准卸载信息、可校验（ICE）、可被企业管理工具识别。

### 5. 安装目录：用户目录，全程不提权

- `%LocalAppData%\Programs\MediaOrganizer`：`<Package Scope="perUser">` + 目录层级 `LocalAppDataFolder → Programs → MediaOrganizer`。
  注意别把 `APPLICATIONFOLDER` 直接挂在 `LocalAppDataFolder` 下——那样会装到 `%LocalAppData%\MediaOrganizer`（真机实测踩过一次）。
- `Scope="perUser"` 保证全程不写 HKLM，受限账户/企业管控环境也能安装。
- 代价：多用户共用一台机器时各自装一份。对本项目（自用/小众分发）可接受。
- **`UpgradeCode` 沿用原 AppId GUID** `7E4C1B52-…`：值不变即可让 MSI 的升级链连上原来那条产品线，与 Inno 的注册表键无技术关联。另加 `<Launch Condition>` 检测旧 Inno 卸载键，挡住"两套卸载器互相删文件"的混合安装。

### 6. 体积治理：沿用既有机制，新增自包含校验

沿用并固化在代码/脚本内（任何发布路径都生效）：

- `MediaOrganizer.Desktop.csproj` 的 `PrunePublishOutput` 目标：Publish 后删除全部 `*.pdb` 与 RID 无关的 `runtimes\` 目录（指定 RID 发布时原生库已扁平化到根目录，`runtimes\` 下其余 7 个平台约 476MB 纯属冗余）。
- `SatelliteResourceLanguages=zh-Hans;en`。
- `publish.ps1` 兜底校验：发布目录不得残留 `.pdb`（AOT 的原生 pdb 由 ILCompiler 在 Publish 之后产出，`PrunePublishOutput` 删不到，脚本把它移入 `packaging/symbols/<RID>/` 保留供崩溃符号解析）；必须存在 `MediaOrganizer.Desktop.exe` 且**不存在** `MediaOrganizer.Core.dll`（后者一旦出现说明 AOT 没生效，发出的是混合布局）。

### 7. 排除的瘦身手段（沿用原 ADR 结论）

| 手段 | 结论 |
| --- | --- |
| `DebugType=none` / `embedded` | 对原生 PDB 无效（见背景 1） |
| `PublishTrimmed` | 已被 NativeAOT 的全量裁剪取代；单独开裁剪只承担风险不获本机代码收益（见决策 3） |
| `PublishSingleFile` | 启动变慢且体积不降反增；安装包本就不暴露目录结构 |
| NativeAOT | ~~原决策 2 否决~~ 2026-10-02 采纳为默认（见决策 2 与修订记录） |
| 删 Linux-only Avalonia 程序集（约 3.3MB） | 收益仅 8%，却要赌 `Avalonia.Win32` 不反射加载它们，不划算 |

## 实测结果（2026-09-05）

| 指标 | 值 |
| --- | --- |
| 发布目录（自包含 + R2R，已剔除符号） | 149.5 MB / 231 个文件 |
| 安装包 `MediaOrganizer_Setup_1.0.0.exe`（LZMA2/ultra64） | **约 47 MB** |
| R2R 生效验证 | 主程序 DLL 含 RTR 标记 |
| 安装过程 | 中文向导，无运行时检测/下载，无提权 |

## 构建流程

```powershell
# 1) 发布（Native AOT，win-x64）
packaging\publish.ps1
#    产物默认落 packaging\publish\win-x64，原生 pdb 移入 packaging\symbols\win-x64

# 2) 打安装包（MSI）
packaging\build-installer.ps1 -Version 1.4.0
#    产物：packaging\releases\MediaOrganizer-windows-x64-1.4.0.msi
```

版本号由 `-Version` 参数给出（CI 传 `$env:APP_VERSION`，本地缺省取 csproj 兜底值）。
注意不能直接 `wix build Product.wxs`：应用文件清单 `obj\Harvest.wxs` 由脚本按发布目录生成，
Product.wxs 还需要 `-d Version / AppFilesGuid / AppIcon` 三个预处理器变量。

## 后果

- 正面：安装即用，零运行时依赖，离线可装；用户目录安装无提权门槛；安装器逻辑极简，失败面小。
- 负面：
  - 安装包约 47 MB，其中运行时占大头（压缩前约 65 MB）。原方案约 24 MB，但需联网补运行时。
  - 每个 RID（win-x64 / win-arm64）各出一包。
  - 放弃 Velopack 即放弃自动更新。
  - 发布目录不含 PDB，线上崩溃栈无行号。如需诊断，单独发布一份带符号的副本归档，不要回退本决策。
- 中性：`Magick.NET`（27 MB，仅用于 HEIC 等格式的 EXIF 读取）去留仍为待决项，结论不影响本 ADR 的形态选择。

## 修订记录（2026-10-02）：桌面发布默认由 ReadyToRun 改为 NativeAOT

决策 2 原以"Avalonia 反射不可控、工作量与崩溃风险不可控"否决 NativeAOT，属纸面推断。本次改为实测驱动，结论翻转：

| 指标 | R2R 自包含（2026-09-05） | Native AOT（2026-10-02） |
| --- | --- | --- |
| 发布目录 | 149.5 MB / 231 个文件 | 67.4 MB / 5 个文件（exe + 4 个原生 dll） |
| 安装包（LZMA2/ultra64） | 约 47 MB | **20.8 MB**（v1.3.0 实测） |
| 窗口可见（冷启动） | 1254 ms | 561 ms |
| 项目代码 IL 警告 | 不适用 | 0 条 IL2026 / IL3050 |

改造面确实只有两处反射：`Desktop/ViewLocator.cs`（动态 `Type.GetType` → 静态 VM→View 工厂登记表）与 System.Text.Json（→ `Core/MediaOrganizerJsonContext.cs` 源生成，经 `AppJson` 提供 `JsonTypeInfo<T>`）。`.NET 10` 的 `JsonSourceGenerationOptions` 没有 `Encoder` 属性，故中文以字面量落盘靠派生 options 覆盖 `UnsafeRelaxedJsonEscaping`，并有防回归测试。

**发版前置检查（每次切 AOT 出包都适用）**：
1. 真机/真实照片库跑一遍日期提取，重点视频（mp4/mov/mkv）——TagLibSharp 的 mimetype 表来自 `Assembly.GetTypes()`，是分析器无法覆盖的 `IL2104` 黑箱。
2. SMB / WebDAV 各连通一次（SMBLibrary、WebDAVClient 同为无标注 netstandard2.0）。
3. 6 个导航页逐一打开确认渲染。

`packaging/publish.ps1` 现只有 AOT 一条路径（R2R 分支与 `-NoReadyToRun`/`-Aot` 开关移除）。若 AOT 在线上暴露裁剪问题，回退方式是回退该提交重新出包，而不是加开关。

## 修订记录（2026-10-04）：打包工具由 Inno Setup 改为 WiX v5（MSI）

决策 4/5 的实现按本节修订，原因与边界：

**为什么换。** `.iss` 静态罗列发布文件， Native AOT 后虽只剩 5 个文件，但依赖变化会改变发布目录内容，
而 `.iss` 既不报错也不给出漏装警告——静默漏装是分发侧最难排查的故障。WiX 侧改成
`build-installer.ps1` 按发布目录 `Get-ChildItem` 的实际结果生成组件清单，多一个少一个都会被后续
验证步骤拦下。附带收益：产物是标准 MSI，"设置 - 应用"里的卸载信息、升级链、企业管控策略识别
都落到 Windows Installer 的标准机制上。

**体积对比。** v1.3.0 实测：发布目录 67.4 MB → MSI **22.9 MB**（Inno + LZMA2/ultra64 时代为 20.8 MB，
约差 2 MB）。这点增量换标准卸载信息与 ICE 校验能力，可接受。

**不可改的东西（漏改则升级会崩）：**
- `UpgradeCode` = `7E4C1B52-3A9D-4E58-9C1F-6D2A8B70F431`（沿用 Inno AppId，保证升级链连通）。
- `build-installer.ps1` 里的 `$AppFilesGuid`（应用文件组件 GUID）：改了以后旧版本文件在升级时不会被清理。
- 安装目录 `%LocalAppData%\Programs\MediaOrganizer`：改了会造成"新 MSI 装到新目录、旧文件留原地"。

**为什么是 perUser 而不是 perMachine / InstallScope 二者可选。** WiX v5 的 `Package/@Scope`
只有 `perUser`/`perMachine` 两个值，没有 v3 的 `"both"`，一个包只能选一种。延续决策 5（不提权）选 perUser，
也因此不能用带"仅我/所有用户"单选框的 `WixUI_Advanced`——选了"所有用户"会让 MSI 去写 HKLM，与 perUser 包模板矛盾。改用 `WixUI_InstallDir`。

**已知的 ICE warning（属预期，勿"修"）：**
- `ICE61`（Maximum version is not less than the current product）：引入 `<MajorUpgrade AllowSameVersionUpgrades="yes">` 的必然结果。CI 对同一版本号重跑发布时需要能原地覆盖。
- `ICE91`（file installed to per-user directory that doesn't vary based on ALLUSERS）：perUser 安装的固有提示，本包不支持 per-machine，无意义。

两者均为 `warning`，`build-installer.ps1` 只在出现 `error` 时 fail。
