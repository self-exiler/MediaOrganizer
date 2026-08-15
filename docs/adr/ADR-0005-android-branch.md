# ADR-0005: Android 分支技术栈与架构

- 状态：已接受（Accepted）
- 日期：2026-08-13
- 关联：ADR-0003（技术架构）、ADR-0004（网络输出目标）、SRS 全篇
- 访谈方式：grill-with-docs 持续追问式访谈（2026-08-13）

## 背景

桌面版（Windows，Avalonia 12 + .NET 10 Core 库）已完善。下一步开发 Android 手机版，让用户在手机上完成媒体文件整理（扫描 → 提取日期 → 分级归档）。

Core 库已做跨平台准备（ADR-0003 实施时即"为远期移动端留路"）：
- Magick.NET 包引用已加 `Condition="'$(TargetFramework)' != 'net10.0-android'"` 条件排除
- ExifExtractor 已用 `#if ANDROID` 预留空实现
- CredentialCrypto 的 DPAPI 分支已加 `OperatingSystem.IsWindows()` 守卫

本 ADR 记录 Android 分支的完整技术决策。

## 决策

### 1. 技术栈

| 项 | 决策 | 备注 |
|----|------|------|
| 运行时 | **.NET 10 (net10.0-android)** | 与桌面版同一运行时 |
| UI 框架 | **Avalonia 12 Android** | 与桌面版同一 UI 框架，ViewModel 可复用 |
| 最低 SDK | **API 33 (Android 13)** | 覆盖面较低但可用 Photo Picker 等最新 API；Scoped Storage 强制 |
| 目标 SDK | API 35 (Android 15) | 跟随主流 |
| 分发 | **APK 侧载** | GitHub Release 分发；无 Play Store 审核约束但仍走 SAF（合规且用户友好） |

### 2. 项目结构

```
MediaOrganizer.slnx
├── src/
│   ├── MediaOrganizer.Core/              # 多目标：net10.0;net10.0-android
│   │   ├── （现有代码不变，#if ANDROID 切换平台相关实现）
│   │   └── Platforms/                    # 新增：平台抽象接口
│   │       ├── ICredentialCrypto.cs      # 凭据加密抽象
│   │       └── IExifReader.cs            # 图片 EXIF 读取抽象
│   ├── MediaOrganizer.Desktop/           # 现有桌面版，不变
│   └── MediaOrganizer.Android/           # 新增：Android 壳项目
│       ├── Views/                        # 全新移动端 XAML（单页 + 抽屉导航）
│       ├── ViewModels/                   # 仅 Android 专属 VM（如 DrawerViewModel）
│       ├── Platforms/
│       │   ├── AndroidCredentialCrypto.cs   # ICredentialCrypto 实现（Keystore）
│       │   ├── AndroidExifReader.cs         # IExifReader 实现（ExifInterface）
│       │   └── AndroidSafStorage.cs         # IFileStorage 实现（SAF + ContentResolver）
│       ├── Services/
│       │   └── AndroidFileScanner.cs       # SAF 树 URI 扫描
│       └── MainActivity.cs
└── test/
    └── MediaOrganizer.Core.Tests/        # 现有，不变
```

Core.csproj 多目标：
```xml
<TargetFrameworks>net10.0;net10.0-android</TargetFrameworks>
```

### 3. 文件访问：SAF + ContentResolver

Android 11+ Scoped Storage 下应用无法直接用文件路径访问共享存储。决策：

- **源目录**：用户通过 SAF 选取目录（`ACTION_OPEN_DOCUMENT_TREE`），返回 `content://` 树 URI
- **目标目录**：同为 SAF 选取的树 URI（本地）或 WebDAV（网络）
- **IFileStorage 接口**：`MediaFile.Path` 存储 `content://` URI 字符串，AndroidSafStorage 内部用 `ContentResolver` + `DocumentFile` 解析（2026-08-15 修订：源参数需抽象化为 IMediaSource，接口并非完全不变，见 ADR-0006 决策 3）
- **FileScanner**：新增 `AndroidFileScanner`，遍历 `DocumentFile.listFiles()` 替代 `Directory.EnumerateFiles()`

### 4. 平台服务注入

Core 新增平台抽象接口，Android 项目实现并在启动时注入：

```csharp
// Core/Platforms/ICredentialCrypto.cs
public interface ICredentialCrypto
{
    string Encrypt(string plain);
    string Decrypt(string stored);
}

// Core/Platforms/IExifReader.cs
public interface IExifReader
{
    DateTimeOffset? ReadImageExif(string path);
}
```

注入点：`AppState` / `CoreFactory` 构造时接收平台实现。现有 `CredentialCrypto` 静态类改为：
- Desktop：`CredentialCrypto` 内部直接用 DPAPI（保持现有行为）
- Android：`AndroidCredentialCrypto` 用 Android Keystore + AES

`ExtractorChain.FromConfig` 增加可选 `IExifReader` 参数；Android 侧注入 `AndroidExifReader`（基于 `Android.Media.ExifInterface`）。

### 5. UI 策略

| 项 | 决策 |
|----|------|
| 导航 | 单页 + 抽屉（Drawer），替代桌面的左侧 Tab 导航 |
| ViewModel | **复用桌面版**：WorkbenchViewModel / FailedFilesViewModel / ReportViewModel / SettingsViewModel |
| View | **全新重写**：移动端布局（窄屏优先、触摸操作、简化工具栏） |
| 魔术工具 | **去除**：手机端不包含魔术工具（交互式圈选不适合触屏，且 Core 的 PatternEngine/PatternInferrer 仍在 Core 库中保留，桌面端不受影响） |
| 日志查看 | **去除**：MVP 不含日志页 |

### 6. 功能范围（MVP）

| 功能 | 桌面版 | Android MVP | 说明 |
|------|--------|-------------|------|
| 整理工作台 | ✅ | ✅ | 扫描 → 分析 → 执行（copy/move） |
| 失败文件管理 | ✅ | ✅ | 列表 + 批量移动到待处理目录 |
| 分析报告 | ✅ | ✅ | 文本统计查看 |
| 设置 | ✅ | ✅ | 精简版（分级/同名策略/提取器权重/网络配置） |
| 魔术工具 | ✅ | ❌ | 触屏不适合圈选交互 |
| 日志查看 | ✅ | ❌ | 非 MVP |
| 缩略图预览 | ✅ | ❌ | 后续迭代 |

### 7. 凭据存储

- **Android**：Android Keystore 生成 AES 密钥，加密凭据后存入 SharedPreferences
- **Desktop**：保持现有 DPAPI（不变）
- Core 的 `CredentialCrypto` 静态类重构为 `ICredentialCrypto` 接口 + 平台实现

### 8. EXIF 读取

- **Android**：`Android.Media.ExifInterface`（API 24+ 支持 HEIC），实现 `IExifReader` 接口
- **Desktop**：保持现有 Magick.NET（不变）
- 视频元数据：两侧共享 TagLibSharp 解析代码，但须通过 StreamFileAbstraction 走流（`TagLib.File.Create(path)` 无法读取 content:// URI）——2026-08-15 修订，见 ADR-0006 决策 2

### 9. 网络存储

| 协议 | Desktop | Android | 说明 |
|------|---------|---------|------|
| 本地/SAF | ✅ LocalFileStorage | ✅ AndroidSafStorage | 各自平台实现 |
| WebDAV | ✅ WebDavFileStorage | ✅ WebDavFileStorage | 纯托管，共享 |
| SMB | ✅ SmbFileStorage (SMBLibrary) | ✅ SmbFileStorage (SMBLibrary) | 2026-08-15 修订（ADR-0007）：激活既有依赖 SMBLibrary 双端统一实现，替代 UNC 方式；仅作输出目标 |

### 10. 配置存储

| 文件 | Desktop 路径 | Android 路径 |
|------|-------------|-------------|
| config.json | `%AppData%/MediaOrganizer/` | `getExternalFilesDir(null)/` |
| patterns.json | 同上 | 同上 |
| analysis-result.json | 同上 | 同上 |

应用专有目录无需存储权限，卸载时自动清除。

## 已完成的跨平台准备（2026-08-13）

在本次访谈前，Core 库已完成以下准备：

1. `MediaOrganizer.Core.csproj`：Magick.NET 包加条件排除
2. `ExifExtractor.cs`：`#if ANDROID` 预留空实现
3. `CredentialCrypto.cs`：DPAPI 分支加 `OperatingSystem.IsWindows()` 守卫，消除 CA1416
4. `SmbFileStorage`：保留（仅 Windows 用，Android 编译时不影响）

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| Avalonia Android 性能/包体未验证 | MVP 先跑通核心流程；包体预计 ~30MB（含 .NET 运行时） |
| SAF 批量文件操作性能（每次 ContentResolver 查询） | AndroidFileScanner 批量查询 + 缓存；大目录分页 |
| ExifInterface 不支持部分冷门格式（TIFF/BMP） | 落入文件名提取器兜底；与桌面版 Magick.NET 覆盖面有差距，可接受 |
| ViewModel 跨平台耦合（桌面版 VM 可能有 Avalonia.Desktop 依赖） | 审查现有 VM，将平台相关逻辑（如 StorageProvider 弹窗）抽到接口 |
| 无 Play Store 分发，用户需手动更新 | GitHub Release + 版本检查；后续可考虑 F-Droid |

## 实施路线（建议）

> 2026-08-15 修订：第 1 步前插入"里程碑 0：源端抽象与共享层重构"（ADR-0006），重构完成、桌面版回归通过后再执行以下步骤。

0. **重构里程碑（ADR-0006）**：IMediaSource 源端抽象、提取/执行链路走流、MediaOrganizer.Shared 抽离、落盘位置改应用专有目录
1. **Core 多目标 + 平台接口**：Core.csproj 加 net10.0-android；抽 ICredentialCrypto / IExifReader；现有 CredentialCrypto 适配
2. **Android 壳项目**：创建 MediaOrganizer.Android；MainActivity + Avalonia Android 初始化
3. **SAF 存储**：AndroidSafStorage + AndroidFileScanner；替换 IFileStorage 注入
4. **核心流程 UI**：工作台 View（扫描/分析/执行）；复用 WorkbenchViewModel
5. **失败文件 + 报告**：复用 VM，写移动端 View
6. **设置 + 网络配置**：精简设置页
7. **凭据 + EXIF**：AndroidCredentialCrypto（Keystore）+ AndroidExifReader（ExifInterface）
8. **打包测试**：APK 侧载验证
