# 桌面版打包

分发形态：**Native AOT 单原生 exe**（不内嵌 CLR，目标机无需安装任何 .NET 组件，离线可装）。
安装目录默认为用户目录（`%LocalAppData%\Programs\MediaOrganizer`），全程不需要管理员权限。
决策依据见 [`docs/adr/ADR-0009-desktop-packaging.md`](../docs/adr/ADR-0009-desktop-packaging.md)（2026-10-02 修订：发布默认由 ReadyToRun 改为 NativeAOT）。

## 快速开始

前置：Inno Setup 6.3+（`winget install -e --id JRSoftware.InnoSetup`）；本机需装 VS C++ 生成工具（AOT 用 MSVC 链接）。

```powershell
# 1) 发布到 packaging/publish/win-x64（Native AOT，自动剔除无用原生符号）
.\publish.ps1
#    可选：.\publish.ps1 -RuntimeIdentifier win-arm64   换目标架构

# 2) 打安装包
ISCC.exe packaging\MediaOrganizer.iss
```

产物在 `packaging/releases/`：

| 文件                               | 说明                                                                      |
| ---------------------------------- | ------------------------------------------------------------------------- |
| `MediaOrganizer-windows-x64-1.3.0.exe` | 单文件安装包，LZMA2 压缩（AOT 实测 20.8MB，R2R 时期约 47MB），中文向导，无运行时依赖，离线可装 |

发新版时改 `MediaOrganizer.iss` 里的 `#define MyAppVersion`。

## 关键选择

- **NativeAOT 而非 ReadyToRun**：反射面只有 ViewLocator 与 System.Text.Json 两处，均已改造为
  静态登记表与源生成上下文；实测发布目录 67.4MB / 冷启动 561ms，对比 R2R 的 149.5MB / 1254ms。
  代价是闭包必须保持裁剪友好，Core/Shared 以 `<IsAotCompatible>` 常驻 IL2026/IL3050 分析器防回归。
- **无独立 PublishTrimmed 开关**：Native AOT 已隐含全量裁剪；在没有 AOT 编译器的路径上单独裁剪只担风险。
- **安装到用户目录**：`DefaultDirName={localappdata}\Programs\MediaOrganizer` +
  `PrivilegesRequired=lowest`，企业受限账户也能安装。
- **Inno Setup 而非 Velopack**：不依赖运行时在线引导，Velopack 的核心理由消失，
  代价是放弃其自动更新通道（v1.0 可接受）。

## 脚本说明

- `publish.ps1` 兜底校验：发布目录不得残留 `.pdb`（AOT 的原生 pdb 由 ILCompiler 在 Publish 之后
  产出，`PrunePublishOutput` 删不到，脚本把它移入 `packaging/symbols/<RID>/` 保留供崩溃符号解析）；
  必须存在 `MediaOrganizer.Desktop.exe` 且不存在 `MediaOrganizer.Core.dll`（后者出现即 AOT 未生效）。
- `Languages/ChineseSimplified.isl` 为随脚本分发的简体中文向导语言包——用户级安装的
  Inno Setup 可能不带非英文语言包，故不引用 `compiler:` 内置路径。
- `MediaOrganizer.iss` 的 `AppId` GUID 不要改，它关联卸载信息与"已安装"识别。

## 已知限制

- 发布目录不含 PDB，线上崩溃栈无行号。原生 pdb 归档在 `packaging/symbols/win-x64/`（已 gitignore），
  诊断时用它符号化，不要改回默认值。
- AOT 产物体积大头是原生库：Magick.Native 约 24MB、libSkiaSharp 约 11.6MB、av_libglesv2 约 5.4MB；
  Magick.NET（仅用于 HEIC 等格式的 EXIF 读取）去留仍为待决项，见 ADR-0009 的"待决"记录。
- **TagLibSharp / SMBLibrary 为无标注 netstandard2.0 库，AOT 下报 IL2104**（分析器看不见其内部）。
  出包前须按 ADR-0009「修订记录」的三步检查跑真实照片库回归，重点是视频日期提取。
- 放弃了自动更新，升级需重新运行安装包（AppId 不变时可原地覆盖安装）。
