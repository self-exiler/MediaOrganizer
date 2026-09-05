# 桌面版打包

分发形态：**框架依赖（运行时外置）**，不自包含、不裁剪。决策依据见
[`docs/adr/ADR-0009-desktop-packaging.md`](../docs/adr/ADR-0009-desktop-packaging.md)。

## 快速开始

```powershell
# 1) 发布到 packaging/publish/win-x64（已自动剔除无用原生符号）
.\publish.ps1

# 2) 打安装包（首次需先装 vpk）
dotnet tool install -g vpk
.\pack-velopack.ps1 -Version 1.0.0
```

产物在 `packaging/releases/`：

| 文件 | 用途 |
| --- | --- |
| `MediaOrganizer-win-x64-Setup.exe` | 单文件安装包，LZMA 压缩，缺失 .NET 10 Runtime 时自动下载 |
| `MediaOrganizer-win-x64-Portable.zip` | 便携版，解压即用（仍需目标机有 .NET 10 Runtime） |
| `*.nupkg` / `RELEASES` | 增量更新源，保留好才能生成后续版本的 delta 包 |

> 发新版时**不要清空 `releases/`**——Velopack 靠目录里的历史版本算增量。

## 运行时说明

Avalonia 只依赖 `Microsoft.NETCore.App`（.NET Runtime），**不需要** .NET Desktop Runtime。
Velopack 打包时已带 `--framework net10.0-x64-runtime`，安装器会自动补环境；
离线机器请手动装 `https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe`。

## 体积实测

| 阶段 | 发布目录 | ZIP(deflate) | 安装包(LZMA, 估) |
| --- | --- | --- | --- |
| 治理前（含 102MB 原生 PDB） | 174 MB | — | — |
| 剔除原生 PDB（已落地） | 74 MB | 31 MB | ~24 MB |
| + 移除 Magick.NET 与 Linux-only 程序集 | 42.7 MB | 17.6 MB | ~14 MB |

PDB 剔除由 `MediaOrganizer.Desktop.csproj` 的 `StripNativeDebugSymbols` 目标完成，
对所有发布路径（VS / CLI / CI）生效，不依赖本目录的脚本。

可调开关：`.\publish.ps1 -NoReadyToRun` 再省约 17 MB，代价是冷启动变慢。

## Magick.NET 处置（待决）

`Magick.NET-Q16-AnyCPU` 在桌面端**只有一个用途**：`MagickExifReader` 读 EXIF 的
`DateTimeOriginal / DateTimeDigitized / DateTime`。它带来 **27 MB**（原生 23 MB + 托管 4 MB），
占总包体积三分之一以上。三个方案：

| 方案 | 体积 | 新增依赖 | 格式覆盖 | 风险 |
| --- | --- | --- | --- | --- |
| **A. 保留 Magick.NET** | +27 MB | 无 | 最广：JPEG/TIFF/PNG/RAW/**HEIC**/WebP… | 无 |
| **B. 改用已有的 TagLib#** | −27 MB | **零新增** | JPEG/TIFF/PNG/主流 RAW，**无 HEIC** | 低：`TagLib.IFD.IFDTag.DateTimeOriginal` 已具备 |
| **C. 引入 MetadataExtractor** | −27 MB | +约 0.4 MB 托管 | JPEG/TIFF/RAW/PNG/WebP，**无 HEIC** | 低 |

- **图库里有 iPhone 的 HEIC 文件 → 选 A**（TagLib# 与 MetadataExtractor 都读不了 HEIC）。
- **全是 JPEG / 单反 RAW → 选 B**，零新增依赖，纯换 `IExifReader` 实现，`ExifExtractor` 与
  `IExifReader` 抽象层（ADR-0006 决策 2）无需改动。

方案 B 的替换骨架（落 `MediaOrganizer.Core/Extraction/`，与 `MagickExifReader` 同层）：

```csharp
// 复用现有 TagLibStreamFileAbstraction；按优先级取 DateTimeOriginal → DateTimeDigitized → DateTime
var abstraction = new TagLibStreamFileAbstraction(name, openRead);
using var file = TagLib.File.Create(abstraction);
if (file.GetTag(TagLib.TagTypes.TiffIFD) is TagLib.IFD.IFDTag exif)
{
    var dt = exif.DateTimeOriginal ?? exif.DateTimeDigitized ?? exif.DateTime;
    // …交给现有 ExifDateParser
}
```

改动范围：`MagickExifReader` → 新实现、`MainWindowViewModel` 里的一处 `new MagickExifReader()`、
`MediaOrganizer.Core.csproj` 摘掉 `Magick.NET-Q16-AnyCPU`（Android TFM 已在本排除，摘掉后该
`Condition` 一并失效可删）。注意 `Core` 目前**没有 EXIF 相关测试**，替换前需先补用例护住行为。

## 备选：Inno Setup

不想依赖 Velopack 时用 `MediaOrganizer.iss`：

```powershell
winget install -e --id JRSoftware.InnoSetup   # 需 6.3+（用到内置 DownloadTemporaryFile）
ISCC.exe packaging\MediaOrganizer.iss
```

脚本自带中文向导、.NET 10 Runtime 注册表检测与缺失时在线下载。

## 已知限制

- 发布目录不含 PDB，线上崩溃栈无行号。需要诊断时单独发布一份带符号的副本，不要改回默认值。
- `PublishTrimmed` 不可用：.NET 10 报 `NETSDK1102`，裁剪要求自包含，与运行时外置互斥。
