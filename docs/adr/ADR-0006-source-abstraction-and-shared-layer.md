# ADR-0006: 源端抽象与共享层重构（Android 移植前置）

- 状态：已接受（Accepted）
- 日期：2026-08-15
- 关联：ADR-0003（技术架构）、ADR-0004（网络输出目标）、ADR-0005（Android 分支）
- 访谈方式：grill-with-docs 持续追问式访谈（2026-08-15，共两轮 8 问）

## 背景

ADR-0005 规划 Android 分支后，对照源码做移植前评估，发现计划与现状存在 4 处脱节和 1 处事实性错误：

1. **ADR-0005 §8 假设错误**：`ExifExtractor.ExtractFromVideo` 调用 `TagLib.File.Create(path)`，需要真实文件路径；Android 上 `MediaFile.Path` 是 `content://` URI，视频元数据在 Android 上完全读不了（"TagLibSharp 纯托管跨平台直接可用"不成立）。
2. **执行链路对"源=本地路径"有硬假设**：`IFileStorage.CopyFromAsync(localSourcePath, …)` 的接口签名、`FileOperator` 中的 `new FileInfo(f.Source.Path).Length`（大小校验）、`File.Exists/File.Delete(f.Source.Path)`（move 语义）三处。ADR-0005 所称"IFileStorage 接口不变"因此不成立。
3. **ViewModel 复用前提不成立**：共享 VM 位于 `MediaOrganizer.Desktop` 项目内，且 `WorkbenchViewModel` 直接调 `App.PickFolderAsync()`、`ReportViewModel`/`MagicToolsViewModel` 直接用 `top.StorageProvider`。Android 无法在不引入 Avalonia.Desktop 依赖的前提下复用。
4. **静默失效点**：`FileSystemExtractor` 的 `File.GetLastWriteTimeUtc(content://…)` 会抛异常被吞（mtime 兜底失效）；`WorkbenchViewModel` 把 `analysis-result.json`/报告 TXT 写到 `Path.Combine(SourceDir, …)`（SAF 源目录写不了）；`Analyzer` 构造函数绑死 `FileScanner` 具体类（无接口，AndroidFileScanner 无法注入）。
5. **业务逻辑散落 VM 层**：`FailedFilesViewModel` 直接 `File.Move` 批量移动失败文件到待处理目录，同样是本地路径假设。

本 ADR 记录移植前置重构的完整决策。

## 决策

### 1. 源端抽象：IMediaSource

Core 新增源端抽象，所有"读源文件"的位置统一走它：

```csharp
// Core/Sources/IMediaSource.cs
public interface IMediaSource
{
    string Identifier { get; }              // 本地路径 或 content:// URI
    string DisplayName { get; }             // 文件名（SAF 下取 DocumentFile.Name）
    long Length { get; }
    DateTimeOffset? ModifiedTime { get; }   // SAF 下扫描时自 DocumentFile.lastModified() 预取
    Stream OpenRead();                      // 本地 FileStream / Android ContentResolver.OpenInputStream
    void Delete();                          // move 语义删除源
}
```

- `LocalMediaSource`（桌面/共享）、`AndroidSafMediaSource`（Android 项目实现）。
- **MediaFile 携带 IMediaSource**：`MediaFile` 增加 `Source` 属性；提取器签名不变（`Extract(MediaFile)`），内部从 `file.Path` 读改为 `file.Source.OpenRead()`。改动集中在模型构造点（Scanner）。
- **保留 `Path` 字段名**，语义改为"源标识符"（本地路径或 content URI）。config.json / analysis-result.json 结构不变，无数据迁移（与 SRS-Android §5.2 已有约定一致）。

### 2. 提取链路改造

| 提取器 | 改造 |
|--------|------|
| ExifExtractor（图片） | 走 `IExifReader.ReadImageExif(Stream)`：桌面 Magick.NET 读流；Android ExifInterface 读流（`ExifInterface(Stream)`，API 24+） |
| ExifExtractor（视频） | **TagLib 流抽象**：自定义 `StreamFileAbstraction(string name, Func<Stream> openRead)` 实现 `TagLib.File.IFileAbstraction`，`TagLib.File.Create(abstraction)` 按扩展名推断解析器。桌面传 FileStream、Android 传 ContentResolver 流——同一份 TagLib 解析代码双端共享，保证结果一致（支撑验收标准"偏差 ≤ 5%"） |
| FileSystemExtractor | 从 `File.GetLastWriteTimeUtc(path)` 改为读 `file.Source.ModifiedTime`；Android 侧扫描时预取，零额外查询 |
| FileNameExtractor | 从 `file.Source.DisplayName` 取文件名（SAF 下 URI 字符串不保证含可读文件名/扩展名） |

### 3. 执行链路改造（修订 ADR-0005 §3"IFileStorage 接口不变"）

- `IFileStorage.CopyFromAsync(string localSourcePath, …)` 改为 `CopyFromAsync(IMediaSource source, …)`（或 `Stream` + 长度，定稿以实现时最小改动为准）。
- `FileOperator` 三处本地路径假设改走抽象：
  - 大小校验：`new FileInfo(path).Length` → `f.Source.Source.Length`（MediaFile 已携带 Length）；
  - move 语义：`File.Exists/File.Delete(path)` → `f.Source.Source.Delete()`；
  - 传输：`_target.CopyFromAsync(f.Source.Source, temp, …)`。
- SAF→SAF 的"移动"语义 = 流式复制 + 删除源（ContentResolver.delete），由实现保证。

### 4. 共享 ViewModel 层：MediaOrganizer.Shared

```
src/
├── MediaOrganizer.Core/       # 多目标 net10.0;net10.0-android（+ IMediaSource 等）
├── MediaOrganizer.Shared/     # 新增，net10.0，Avalonia 无头（仅 MVVM 依赖）
│   ├── ViewModels/            # Workbench/FailedFiles/Report/Settings 四个共享 VM（自 Desktop 迁入）
│   └── Services/
│       ├── IFolderPicker.cs   # 目录选取抽象（桌面 StorageProvider / Android SAF Intent）
│       └── IFileSaver.cs      # 报告另存为抽象
├── MediaOrganizer.Desktop/    # 保留：魔术工具 VM、日志 VM、平台对话框实现
└── MediaOrganizer.Android/    # 新建（ADR-0005）：SAF 选取实现、Android VM、Views
```

- 共享 VM 内 `App.PickFolderAsync()` / `top.StorageProvider` 调用全部替换为注入的 `IFolderPicker` / `IFileSaver`。
- `WorkbenchViewModel` 中的结果落盘逻辑随决策 6 下沉到 Core（`AnalysisResultStore` 扩展）。

### 5. 失败文件批量移动下沉 Core

- 新增 Core 服务 `PendingFileMover`：批量移动失败文件到待处理目录，复用 IMediaSource（源）+ IFileStorage（目标）；同名冲突沿用 rename `_n` 规则（复用 `FileOperator.FindFreeLocalPath` 的语义，抽象化后双端共享）。
- `FailedFilesViewModel` 只调服务，不再直接 `File.Move`。

### 6. 分析结果落盘位置：应用专有目录（两端统一）

- `analysis-result.json` 与 `analysis-report.txt` 改为写入应用数据目录（桌面 `%AppData%/MediaOrganizer/`，Android `getExternalFilesDir(null)`），**不再写源目录**。
- 桌面版行为变化（报告不再出现在源目录），属有意变更；用户导出走"另存为/分享"（Report 页 `IFileSaver`，Android 侧走 SAF `ACTION_CREATE_DOCUMENT` 或系统分享）。
- SRS-Android §5.2、FR-A4.2 相应修订。

### 7. 实施路线：重构先行（修订 ADR-0005 实施路线）

在 ADR-0005 路线第 1 步之前插入"里程碑 0：重构"，桌面版行为不变、现有测试全绿后再动 Android：

0. **重构里程碑**（本 ADR）：
   a. Core 引入 IMediaSource + LocalMediaSource；MediaFile 携带 Source；Scanner 构造源
   b. 提取链路改走流（IExifReader(Stream)、TagLib StreamFileAbstraction、ModifiedTime/DisplayName）
   c. IFileStorage.CopyFromAsync 源参数抽象化；FileOperator 三处改走抽象
   d. 落盘位置改应用专有目录；PendingFileMover 下沉
   e. 抽 MediaOrganizer.Shared，迁移 4 个 VM，引入 IFolderPicker/IFileSaver
   f. 桌面版回归：全部现有测试通过 + 手工跑通完整整理流程
1.~8. 按 ADR-0005 原路线执行（Core 多目标 → Android 壳 → SAF 存储 → UI → …），其中第 1 步的"IExifReader"已在本里程碑落地，多目标时仅需新增 Android 实现。

### 8. 杂项（多目标时一并处理）

- `MediaOrganizer.Core.csproj` 改 `<TargetFrameworks>net10.0;net10.0-android</TargetFrameworks>`。
- `System.Security.Cryptography.ProtectedData` 包引用加 `Condition="'$(TargetFramework)' != 'net10.0-android'"` 排除（CA1416 噪音）；对应源码文件用 `#if !ANDROID` 或条件编译项排除。
- ~~SMBLibrary 加条件排除~~（2026-08-15 作废）：ADR-0007 决定激活 SMBLibrary 双端支持 SMB，包引用全平台保留。

## 对既有文档的修订

| 文档 | 修订点 |
|------|--------|
| ADR-0005 §3 | "IFileStorage 接口不变" → 源参数抽象化（本 ADR 决策 3） |
| ADR-0005 §8 | "视频元数据两侧均用 TagLibSharp" → TagLib 流抽象（本 ADR 决策 2） |
| ADR-0005 实施路线 | 前置插入里程碑 0（本 ADR 决策 7） |
| SRS-Android §2.5 | TagLib 假设修正 |
| SRS-Android FR-A2.3 / FR-A4.2 / FR-A6.5 / §5.2 | 视频流抽象、落盘位置、失败文件移动下沉 |

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| 重构面大（提取/执行/VM 三层同改），桌面版回归 | 里程碑 0 以"现有测试全绿 + 手工流程回归"为完成标准；重构期间不加任何新功能 |
| TagLib StreamFileAbstraction 对部分容器（mkv/avi）的随机访问需求 | StreamFileAbstraction 包装为可 seek 流；SAF OpenInputStream 支持 seek（AssetFileDescriptor）；不可 seek 的提供者降级拷缓存 |
| MediaFile 携带 IMediaSource 后，analysis-result.json 序列化泄露平台对象 | Source 属性标 `[JsonIgnore]`；反序列化时由 Scanner/Store 按 Path 重新解析源 |
| Shared 项目迁入 VM 后 Desktop 编译断点 | 一次性搬迁 + 命名空间保留（`MediaOrganizer.Shared.ViewModels`），Desktop 引用 Shared |
