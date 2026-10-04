# 桌面版打包

分发形态：**Native AOT 单原生 exe**（不内嵌 CLR，目标机无需安装任何 .NET 组件，离线可装），
外包装为标准 **MSI**（WiX v5）。安装目录默认为用户目录（`%LocalAppData%\Programs\MediaOrganizer`），
全程不需要管理员权限。

决策依据见 [`docs/adr/ADR-0009-desktop-packaging.md`](../docs/adr/ADR-0009-desktop-packaging.md)
（2026-10-02 修订：发布由 ReadyToRun 改为 NativeAOT；2026-10-04 修订：打包由 Inno Setup 改为 WiX v5）。

## 快速开始

前置（一次性）：

```powershell
dotnet tool install --global wix --version 5.0.2
wix extension add -g WixToolset.UI.wixext/5.0.2
```

本机还需装 VS C++ 生成工具（AOT 用 MSVC 链接）。若 `wix` 不在 PATH 上，
把 `%USERPROFILE%\.dotnet\tools` 加进去。

日常出包：

```powershell
# 1) 发布到 packaging/publish/win-x64（Native AOT，自动剔除无用原生符号）
.\publish.ps1
#    可选：.\publish.ps1 -RuntimeIdentifier win-arm64   换目标架构

# 2) 打 MSI
.\build-installer.ps1 -Version 1.4.0
```

产物在 `packaging/releases/`：

| 文件                                     | 说明                                                                             |
| ---------------------------------------- | -------------------------------------------------------------------------------- |
| `MediaOrganizer-windows-x64-<版本>.msi`  | 标准 MSI，内嵌 cab + 高压缩（实测 22.9 MB），中文向导，无运行时依赖，离线可装     |
| `MediaOrganizer-windows-x64-<版本>.wixpdb` | WiX 构建副产物（符号/补丁元数据），随 msi 同目录产出，**不对外分发**            |

版本号由 `-Version` 给出（CI 传 tag 剥掉前导 `v` 的结果；本地省略则取 csproj 兜底值）。

## 关键选择

- **NativeAOT 而非 ReadyToRun**：反射面只有 ViewLocator 与 System.Text.Json 两处，均已改造为
  静态登记表与源生成上下文；实测发布目录 67.4MB / 冷启动 561ms，对比 R2R 的 149.5MB / 1254ms。
  代价是闭包必须保持裁剪友好，Core/Shared 以 `<IsAotCompatible>` 常驻 IL2026/IL3050 分析器防回归。
- **无独立 PublishTrimmed 开关**：Native AOT 已隐含全量裁剪；在没有 AOT 编译器的路径上单独裁剪只担风险。
- **MSI（WiX v5）而非 Inno Setup**：见 ADR-0009 决策 4 与 2026-10-04 修订记录。核心理由是
  `.iss` 静态罗列发布文件，依赖一变就静默漏装；MSI 侧由脚本按发布目录实际内容生成清单，
  多文件少文件都会被拦下，并换来标准卸载信息与 ICE 校验。
- **安装到用户目录**：`<Package Scope="perUser">` + `LocalAppDataFolder → Programs → MediaOrganizer`，
  企业受限账户也能安装。也因此不能用带"仅我/所有用户"单选框的 `WixUI_Advanced`（选"所有用户"
  会让 MSI 去写 HKLM，与 perUser 包模板矛盾），改用 `WixUI_InstallDir`。
- **放弃了自动更新**，升级需重新运行安装包（`UpgradeCode` 不变时可原地覆盖安装）。

## 脚本说明

### `publish.ps1`

- 兜底校验：发布目录不得残留 `.pdb`（AOT 的原生 pdb 由 ILCompiler 在 Publish 之后产出，
  `PrunePublishOutput` 删不到，脚本把它移入 `packaging/symbols/<RID>/` 保留供崩溃符号解析）；
  必须存在 `MediaOrganizer.Desktop.exe` 且不存在 `MediaOrganizer.Core.dll`（后者出现即 AOT 未生效）。

### `build-installer.ps1`

`Product.wxs` **不能直接 `wix build`**，必须先跑本脚本，它负责三件事：

1. 按 `PublishDir` 的实际文件生成 `obj/Harvest.wxs`（应用文件清单）。依赖变化自动跟随，
   不像 `.iss` 那样需要手工同步。出现子目录会直接报错——当前打包模型只支持扁平布局。
2. 传入预处理器变量：`Version` / `AppFilesGuid` / `AppIcon`（图标以绝对路径传，
   因为 `wix build` 按调用者 CWD 解析相对路径，从仓库根调用会失效）。
3. `wix build` + `wix msi validate`（ICE 校验，**有 error 即失败**）。

`$AppFilesGuid` 与 `Product.wxs` 的 `UpgradeCode` **永不可改**——前者改了以后升级不会清理旧版本文件，
后者改了会断掉升级链。详见 ADR-0009 的 2026-10-04 修订记录。

## 已知限制

- 发布目录不含 PDB，线上崩溃栈无行号。原生 pdb 归档在 `packaging/symbols/win-x64/`（已 gitignore），
  诊断时用它符号化，不要改回默认值。
- AOT 产物体积大头是原生库：Magick.Native 约 24MB、libSkiaSharp 约 11.6MB、av_libglesv2 约 5.4MB；
  Magick.NET（仅用于 HEIC 等格式的 EXIF 读取）去留仍为待决项，见 ADR-0009 的"待决"记录。
- **TagLibSharp / SMBLibrary 为无标注 netstandard2.0 库，AOT 下报 IL2104**（分析器看不见其内部）。
  出包前须按 ADR-0009「修订记录」的三步检查跑真实照片库回归，重点是视频日期提取。
- ICE 校验固定输出两条 warning，属预期、不要去"修"：
  - `ICE61`：`AllowSameVersionUpgrades="yes"` 的必然结果（同一版本号重跑发布要能原地覆盖）。
  - `ICE91`：perUser 安装的固有提示，本包不支持 per-machine。
- 桌面快捷方式默认不创建，命令行开：`msiexec /i MediaOrganizer.msi MO_DESKTOP_SHORTCUT=1`。
