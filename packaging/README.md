# 桌面版打包

分发形态：**自包含（内嵌 .NET 10 Runtime）+ ReadyToRun 预编译**，目标机无需安装任何 .NET 组件，离线可装。
安装目录默认为用户目录（`%LocalAppData%\Programs\MediaOrganizer`），全程不需要管理员权限。
决策依据见 [`docs/adr/ADR-0009-desktop-packaging.md`](../docs/adr/ADR-0009-desktop-packaging.md)（重写版）。

## 快速开始

前置：Inno Setup 6.3+（`winget install -e --id JRSoftware.InnoSetup`）。

```powershell
# 1) 发布到 packaging/publish/win-x64（自包含 + R2R，自动剔除无用原生符号）
.\publish.ps1
#    可选：.\publish.ps1 -NoReadyToRun   关掉 R2R，省约 17MB（启动略慢）
#    可选：.\publish.ps1 -RuntimeIdentifier win-arm64   换目标架构

# 2) 打安装包
ISCC.exe packaging\MediaOrganizer.iss
```

产物在 `packaging/releases/`：

| 文件                               | 说明                                                                      |
| ---------------------------------- | ------------------------------------------------------------------------- |
| `MediaOrganizer-windows-x64-1.0.0.exe` | 单文件安装包，LZMA2 压缩（实测约 47MB），中文向导，无运行时依赖，离线可装 |

发新版时改 `MediaOrganizer.iss` 里的 `#define MyAppVersion`。

## 关键选择

- **ReadyToRun 而非 NativeAOT**：Avalonia 大量依赖反射与编译绑定（XAML、MVVM、转换器），
  NativeAOT 需要全量裁剪标注，风险与成本高；R2R 只把 IL 预编译为本机代码，不做裁剪，
  行为与 JIT 完全一致，是安全默认值。
- **不启用 PublishTrimmed**：自包含后技术上可用，但裁剪对 Avalonia 反射路径极不友好，容易运行时崩溃。
- **安装到用户目录**：`DefaultDirName={localappdata}\Programs\MediaOrganizer` +
  `PrivilegesRequired=lowest`，企业受限账户也能安装。
- **Inno Setup 而非 Velopack**：自包含后不再需要 Velopack 的运行时在线引导，
  代价是放弃其自动更新通道（v1.0 可接受）。

## 脚本说明

- `publish.ps1` 兜底校验：发布目录不得残留 `.pdb`（原生符号清理由
  `MediaOrganizer.Desktop.csproj` 的 `PrunePublishOutput` 目标完成），且必须存在
  `coreclr.dll`（确认确为自包含产物）。
- `Languages/ChineseSimplified.isl` 为随脚本分发的简体中文向导语言包——用户级安装的
  Inno Setup 可能不带非英文语言包，故不引用 `compiler:` 内置路径。
- `MediaOrganizer.iss` 的 `AppId` GUID 不要改，它关联卸载信息与"已安装"识别。

## 已知限制

- 发布目录不含 PDB，线上崩溃栈无行号。需要诊断时单独发布一份带符号的副本，
  不要改回默认值。
- 安装包体积主要来自内嵌运行时（压缩前约 65MB）与 SkiaSharp/Magick.NET 原生库；
  Magick.NET（约 27MB，仅用于 EXIF 读取）去留为待决项，见 ADR-0009 的"待决"记录。
- 放弃了自动更新，升级需重新运行安装包（AppId 不变时可原地覆盖安装）。
