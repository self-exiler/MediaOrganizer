# ADR-0009: 桌面版分发与打包策略（自包含 + ReadyToRun）

- 状态：已接受（Accepted）——重写版，取代 2026-09-01 的"运行时外置"决策
- 日期：2026-09-05
- 关联：ADR-0006（Core/Shared/Desktop 分层）、ADR-0008（v1.0 范围冻结）
- 关联文件：`packaging/publish.ps1`、`packaging/MediaOrganizer.iss`、`packaging/README.md`

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

### 2. 预编译：ReadyToRun，明确不采用 NativeAOT

选 **`PublishReadyToRun=true`**：

- R2R 只是把 IL 预编译成本机代码，**不做裁剪**，运行行为与 JIT 完全一致，是零风险换冷启动的开关。
- **NativeAOT 不采用**：Avalonia 依赖大量反射与编译绑定（XAML、`CompiledBindings`、MVVM、转换器、ViewLocator），AOT 要求全量反射标注与裁剪友好改造，工作量和运行时崩溃风险都不可控，且本项目无"禁用 JIT"类的硬约束支撑这笔投入。

### 3. 不启用 PublishTrimmed

自包含后裁剪在技术上可用（原 ADR 记录的 `NETSDK1102` 互斥问题随自包含消失），但裁剪对 Avalonia 的反射路径极不友好，容易运行时崩溃。体积治理仍走"剔除无用文件 + 高压缩"路线，不走裁剪。

### 4. 打包工具：Inno Setup，弃用 Velopack

主路线改为 **Inno Setup**（6.3+，本机为用户级安装于 `%LocalAppData%\Programs\Inno Setup 6`）：

- 自包含后不再需要 Velopack 的 `--framework` 运行时引导——那是选它的核心理由，理由已消失。
- Inno 提供中文向导（`ChineseSimplified.isl`，置于 `packaging/Languages/` 随脚本走，因用户级安装可能不带非英文语言包）、LZMA2/ultra64 压缩、标准卸载体验。
- 弃用 Velopack 同时意味着**放弃自动更新通道**；v1.0 范围内可接受，后续若要增量更新可重新评估。

### 5. 安装目录：用户目录，全程不提权

- `DefaultDirName={localappdata}\Programs\MediaOrganizer`（即 `%LocalAppData%\Programs\MediaOrganizer`）
- `PrivilegesRequired=lowest`：受限账户/企业管控环境也能安装。
- 代价：多用户共用一台机器时各自装一份。对本项目（自用/小众分发）可接受。

### 6. 体积治理：沿用既有机制，新增自包含校验

沿用并固化在代码/脚本内（任何发布路径都生效）：

- `MediaOrganizer.Desktop.csproj` 的 `PrunePublishOutput` 目标：Publish 后删除全部 `*.pdb` 与 RID 无关的 `runtimes\` 目录（指定 RID 发布时原生库已扁平化到根目录，`runtimes\` 下其余 7 个平台约 476MB 纯属冗余）。
- `SatelliteResourceLanguages=zh-Hans;en`。
- `publish.ps1` 兜底校验：发布目录不得残留 `.pdb`；必须存在 `coreclr.dll`（确认确为自包含产物）。

### 7. 排除的瘦身手段（沿用原 ADR 结论）

| 手段 | 结论 |
| --- | --- |
| `DebugType=none` / `embedded` | 对原生 PDB 无效（见背景 1） |
| `PublishTrimmed` | Avalonia 反射风险（见决策 3） |
| `PublishSingleFile` | 启动变慢且体积不降反增；安装包本就不暴露目录结构 |
| NativeAOT | 见决策 2 |
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
# 1) 发布（自包含 + R2R）
packaging\publish.ps1
#    可选：packaging\publish.ps1 -NoReadyToRun   关掉 R2R，省约 17MB（启动略慢）

# 2) 打安装包
ISCC.exe packaging\MediaOrganizer.iss
#    产物：packaging\releases\MediaOrganizer_Setup_<版本>.exe
```

版本号在 `packaging/MediaOrganizer.iss` 的 `#define MyAppVersion` 维护。

## 后果

- 正面：安装即用，零运行时依赖，离线可装；用户目录安装无提权门槛；安装器逻辑极简，失败面小。
- 负面：
  - 安装包约 47 MB，其中运行时占大头（压缩前约 65 MB）。原方案约 24 MB，但需联网补运行时。
  - 每个 RID（win-x64 / win-arm64）各出一包。
  - 放弃 Velopack 即放弃自动更新。
  - 发布目录不含 PDB，线上崩溃栈无行号。如需诊断，单独发布一份带符号的副本归档，不要回退本决策。
- 中性：`Magick.NET`（27 MB，仅用于 HEIC 等格式的 EXIF 读取）去留仍为待决项，结论不影响本 ADR 的形态选择。
