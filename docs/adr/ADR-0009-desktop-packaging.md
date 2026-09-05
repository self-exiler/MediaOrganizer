# ADR-0009: 桌面版分发与打包策略（运行时外置）

- 状态：已接受（Accepted）
- 日期：2026-09-01
- 关联：ADR-0006（Core/Shared/Desktop 分层）、ADR-0008（v1.0 范围冻结）
- 关联文件：`packaging/publish.ps1`、`packaging/pack-velopack.ps1`、`packaging/MediaOrganizer.iss`

## 背景

桌面版功能趋于稳定，进入可交付阶段。首次按现有 `FolderProfile` 实测发布产物，结果不可用：

| 指标 | 实测值 |
| --- | --- |
| 发布目录体积 | **174 MB** |
| 文件数 | 49 |
| 其中 `libSkiaSharp.pdb` | 82 MB |
| 其中 `libHarfBuzzSharp.pdb` | 20 MB |
| 其中 `Magick.Native-Q16-x64.dll` | 23 MB |

即 **59% 的体积是终端用户完全用不到的原生调试符号**。根因：`SkiaSharp.NativeAssets.Win32` 与 `HarfBuzzSharp.NativeAssets.Win32` 把 `libSkiaSharp.pdb` / `libHarfBuzzSharp.pdb` 放在 `runtimes/win-x64/native/` 下，SDK 作为原生运行时资产原样拷贝——**不受 `DebugType` 控制**，因此改 `DebugType=none` 无效。

另有两个约束性发现：

1. **`PublishTrimmed` 与运行时外置互斥**。实测 `-p:PublishTrimmed=true --self-contained false` 直接报 `NETSDK1102: 所选发布配置不支持优化程序集的大小。请确保你发布的是独立应用。` 想要裁剪就必须自包含，二者不可兼得。
2. **Avalonia 只依赖 `Microsoft.NETCore.App`**。发布产物的 `runtimeconfig.json` 只声明 `Microsoft.NETCore.App 10.0.0`，**不含 `Microsoft.WindowsDesktop.App`**。因此用户只需装体积更小的 .NET Runtime，不是 .NET Desktop Runtime（WPF/WinForms 才需要后者）。

## 决策

### 1. 分发形态：框架依赖（运行时外置），不自包含

`--self-contained false`，运行时由安装器按需引导下载。理由：

- 自包含需额外 ~65MB 运行时，且对每个目标架构各发一份；本项目面向自用与开源小众分发，不值得。
- 运行时外置后可用 Velopack / Inno 的 `--framework` 引导，首次安装自动补环境，用户体验不打折。
- 代价是必须放弃 `PublishTrimmed`（见背景 1）。本决策下体积治理只能靠"剔除无用文件 + 压缩"，不能靠裁剪。

### 2. 打包工具：Velopack 为主，Inno Setup 为备

- **主路线 Velopack**（`dotnet tool install -g vpk`）。纯 dotnet 工具链，不引入 Inno/NSIS/WiX 等外部安装器依赖；产出 LZMA 压缩的单文件 `Setup.exe` + 便携 zip + 增量更新包；`--framework net10.0-x64-runtime` 原生支持运行时引导；自带自动更新 API。
- **备选 Inno Setup**（`winget install -e --id JRSoftware.InnoSetup`，需 6.3+）。脚本化、中文向导、LZMA2 压缩，运行时检测走注册表 `SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App`。保留为备用的原因是 Velopack 为第三方活跃项目，留一条不依赖它的退路。

不采用：MSIX（需签名、分发受商店/策略约束）、WiX/MSI（脚本成本高）、Squirrel.Windows（已停止维护，Velopack 是其继任者）。

### 3. 体积治理：在 csproj 内固化，而非靠外部脚本

`MediaOrganizer.Desktop.csproj` 增加：

- `StripNativeDebugSymbols` 目标（`AfterTargets="Publish"`）删除发布目录全部 `*.pdb`。这是本 ADR 收益最大的一项，**174 MB → 74 MB**。放在 csproj 而非打包脚本里，是因为任何发布路径（VS 发布、CLI、CI）都会走到。
- `SatelliteResourceLanguages=zh-Hans;en`。v1.0 单一中文界面（ADR-0008 决策 2），无谓的卫星资源程序集不该进包。

### 4. 排除的瘦身手段（记录以免重复尝试）

| 手段 | 结论 |
| --- | --- |
| `DebugType=none` / `embedded` | 对原生 PDB 无效（见背景） |
| `PublishTrimmed` | `NETSDK1102`，与运行时外置互斥 |
| `PublishSingleFile` | 框架依赖下不支持压缩，体积不降反增，且拖慢启动；安装包本来就不暴露目录结构 |
| 删 Linux-only Avalonia 程序集（X11 / FreeDesktop / AtSpi / Tmds.DBus，约 3.3MB） | 收益仅 8%，却要赌 `Avalonia.Win32` 不反射加载它们。收益风险比不划算，暂不做 |

### 5. 保留可调开关：ReadyToRun

`PublishReadyToRun=true` 使体积 +约 17 MB（74 MB → 91 MB 裸 IL 对应值），换来更快的冷启动。`publish.ps1` 提供 `-NoReadyToRun` 开关，需要极限体积时关掉。默认保留。

### 6. 待决（Open）：Magick.NET 的去留

Magick.NET 在桌面端**仅用于 `MagickExifReader` 读 EXIF 拍摄时间**，却带来 27 MB（原生 23 MB + 托管 4 MB）。三个方案尚未定夺，见 `packaging/README.md` §Magick.NET 处置。本 ADR 不强行决策，因涉及格式覆盖面的产品权衡而非纯技术权衡。

## 实测收益

| 阶段 | 发布目录 | ZIP(deflate) |
| --- | --- | --- |
| 现状（含 102MB 原生 PDB） | 174 MB | — |
| ① 剔除原生 PDB（已落地） | **74 MB** | **31 MB** |
| ② ①+ 移除 Magick.NET 与 Linux-only 程序集（方案模拟） | **42.7 MB** | **17.6 MB** |

① 已验证落地。② 为删除文件后的模拟值，尚未改代码。Inno/Velopack 用 LZMA/LZMA2，通常比 deflate 再小 15–25%，故最终安装包体量预计：

- 仅做 ①：**约 24 MB**
- 做到 ②：**约 14 MB**

## 后果

- 正面：发布产物从不可用（174MB）降到可分发（74MB / 安装包约 24MB）；打包流程固化为两个脚本，可接入 CI。
- 负面：
  - 首次安装若目标机无 .NET 10 Runtime 需联网下载。离线场景需改用自包含发布（与决策 1 冲突，届时走 `packaging/publish.ps1` 之外的独立流程）。
  - 发布目录不再含 PDB，线上崩溃只能拿到无行号的栈。如需诊断，改为发布到单独目录自行归档符号，不要回退本决策。
- 中性：`Magick.NET` 去留未定，在其定论前 ② 不成立，安装包停在约 24 MB 一档。
