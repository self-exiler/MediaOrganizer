# 架构深化分析报告 (Architecture Review)

> 审查日期: 2026-08-21
> 审查范围: 全代码库
> 分析方法: improve-codebase-architecture skill（Ousterhout 深度/浅度分析）

---

## 项目架构概览

```
MediaOrganizer.Desktop ──────┐
MediaOrganizer.Android ──────┤
      │                      │
      ▼                      ▼
MediaOrganizer.Shared (ViewModel层)
      │
      ▼
MediaOrganizer.Core (零 UI 依赖)
  ├── Analysis/    扫描→提取→汇总
  ├── Configuration/  配置读写
  ├── Execution/   归档执行
  ├── Extraction/  日期提取器
  ├── Logging/     内存日志
  ├── Models/      领域模型
  ├── Patterns/    模式引擎
  ├── Planning/    归档规划
  ├── Platforms/   平台抽象接口
  ├── Scanning/    文件扫描
  ├── Security/    凭据加密
  ├── Sources/     源端抽象
  └── Storage/     目标存储
```

当前架构四层分明：Core → Shared → Desktop/Android。Core 内部按领域子目录组织，每个子目录 1-3 个文件。整体 **module 数量合理但粒度偏碎**，多个目录仅含一个实现类和一个接口。

---

## 深化候选项

### 候选项 1: 合并 Configuration 四文件为一个深 module

- **涉及文件**: `AppConfig.cs`、`ConfigManager.cs`、`JsonFileStore.cs`、`PatternsStore.cs`
- **问题**: Configuration 目录有 4 个文件，但 `ConfigManager` 和 `JsonFileStore` 都是 shallow modules：`ConfigManager` 的 interface 就是两个一行方法，几乎与 `JsonFileStore` 一样复杂。`PatternsStore` 有实质逻辑（内置模式列表），但其 Load/Save 也只是 `JsonFileStore` 的包装。理解"配置如何读写"需要在 4 个文件间跳转（locality 差）。
- **方案**: 将 `ConfigManager` 和 `JsonFileStore` 合并为一个 `ConfigStore` deep module，对外只暴露 `LoadConfig`/`SaveConfig`/`LoadPatterns`/`SavePatterns`/`Load<T>`/`Save<T>` 的简洁 interface，将 `JsonOptions`、原子写入等 implementation 完全隐藏。
- **收益**:
  - **locality**: 配置持久化逻辑集中在一个模块
  - **leverage**: 一个 interface 服务 AppState、AnalysisResultStore 等所有持久化调用点
  - interface 从 3 个类 × 2 方法 → 1 个类 × 少量方法
- **推荐强度**: `Strong`
- **Before / After**:
  - Before: `AppState → ConfigManager → JsonFileStore`；`AnalysisResultStore → JsonFileStore → ConfigManager.JsonOptions`（循环依赖气味）
  - After: `AppState → ConfigStore`；`AnalysisResultStore → ConfigStore`（单一入口）

---

### 候选项 2: 将 OrganizeSession 深化为真正的会话 Facade

- **涉及文件**: `OrganizeSession.cs`、`WorkbenchViewModel.cs`、`CoreFactory.cs`
- **问题**: `OrganizeSession` 编排 "分析→规划→执行" 流程，但 **分析 (Analyzer) 的创建和调用仍在 ViewModel 层**（`WorkbenchViewModel.AnalyzeAsync()` 自行调用 `_analyzerFactory()` 和 `analyzer.AnalyzeAsync()`），Session 仅接收 `SetResult()`。这导致 "分析" 和 "规划→执行" 被 seam 切开，locality 破碎——理解完整流程需要同时看 Core 和 Shared 两层。
- **方案**: 将 `Analyzer` 创建和分析调用收进 `OrganizeSession`，使其成为真正的完整流程 facade。ViewModel 只调 `Session.AnalyzeAsync(sourceDir, progress, ct)` 和 `Session.ExecuteAsync(progress, ct)`。
- **收益**:
  - **locality**: "分析→规划→执行" 完整流程集中在一个 Core module
  - **leverage**: Desktop 和 Android 的 WorkbenchViewModel 都简化为 thin caller
  - **depth**: Session 的 interface 更窄（2 个方法），implementation 更丰富
- **推荐强度**: `Strong`
- **Before / After**:
  - Before: `WorkbenchVM` 负责创建 Analyzer、调用 AnalyzeAsync、保存结果到 JSON、设置 Session 结果（≥50 行编排）
  - After: `WorkbenchVM` 调 `Session.AnalyzeAsync()` 一行，Session 内部完成扫描→提取→持久化→规划

---

### 候选项 3: 合并 Storage + Sources 为统一的文件 I/O module

- **涉及文件**: `IFileStorage.cs`、`LocalFileStorage.cs`、`SmbFileStorage.cs`、`WebDavFileStorage.cs`、`IMediaSource.cs`、`LocalMediaSource.cs`、`MemoryMediaSource.cs`、`StorageFactory.cs`
- **问题**: `Sources/`（源端读取）和 `Storage/`（目标端写入）是两个 seam，但 `IFileStorage.CopyFromAsync` 的源参数就是 `IMediaSource`——两者天然耦合。`StorageFactory` 同时涉及目标创建和源端钩子（`CustomLocalStorageFactory`），是两个 seam 的交叉点。理解 "文件从哪来到哪去" 需要在 Sources + Storage + StorageFactory 三个位置跳转。
- **方案**: 将 Sources 和 Storage 合并为 `IO/` module。`IMediaSource` 和 `IFileStorage` 保持独立接口，但物理上同居一个 namespace。`StorageFactory` 改名 `FileIOFactory` 统一管理两端。
- **收益**:
  - **locality**: 文件 I/O 的源端和目标端逻辑在同一目录
  - 删除 `Sources/` 目录后，3 个小文件合入 `IO/`，目录数 -1
- **推荐强度**: `Worth exploring`
- **Before / After**:
  - Before: `Sources/` (3 文件) + `Storage/` (4 文件) + `StorageFactory.cs` = 8 文件 3 目录
  - After: `IO/` (8 文件) = 同样的代码量，但 1 个目录，locality 提升

---

### 候选项 4: 深化 Patterns module — 将 PatternEngine + PatternInferrer 统一 interface

- **涉及文件**: `PatternEngine.cs`、`PatternInferrer.cs`、`StructureFingerprint.cs`
- **问题**: `PatternEngine` 和 `PatternInferrer` 都是 static class，对外 interface 几乎一样宽（大量 public static 方法）。`StructureFingerprint` 仅 13 行，是一个 shallow utility。`MagicToolsViewModel` (597 行) 调用这三个类的多个方法，调用关系分散。
- **方案**: 将三者合并为一个 `PatternService` deep module（可非 static），对外暴露：`TryExtract(fileName, patterns)`、`ContainsLikelyDate()`、`Infer(regex, samples)`、`GenerateFromMarks()`、`ComputeFingerprint()`。内部细节隐藏。
- **收益**:
  - **interface 收窄**: 从 3 个 static class × 多个 public 方法 → 1 个 module × 5 个方法
  - **depth**: implementation 丰富（正则缓存、推断逻辑、指纹），interface 简洁
- **推荐强度**: `Worth exploring`
- **Before / After**:
  - Before: `MagicToolsVM` 需 `using PatternEngine; using PatternInferrer; using StructureFingerprint;`
  - After: `MagicToolsVM` 仅 `using PatternService;`

---

### 候选项 5: 将 MagicToolsViewModel 从 Desktop 移入 Shared

- **涉及文件**: `MagicToolsViewModel.cs`（Desktop）、`Shared/` 项目
- **问题**: SRS 和 ADR-0006 明确说"魔术工具 Android 不含"，所以 `MagicToolsViewModel` 目前在 Desktop 项目。但这个 ViewModel 有 597 行，其中核心逻辑（样本加载、标记生成正则、实时测试、保存模式）全部是 Core/Patterns 的 thin wrapper，**唯一的 Desktop 依赖是 `App.MainWindow`（用于文件选择器）**。这意味着如果将 `BrowseJsonFile` 提取为 `IFilePicker` 注入，整个 VM 就可以移入 Shared。
- **方案**: 将 `BrowseJsonFile` 中的 `StorageProvider` 依赖提取为注入接口，VM 移入 Shared。Android 端不注入该 VM 即可。
- **收益**:
  - **leverage**: 未来若 Android 也要支持简化版魔术工具，VM 即可复用
  - 减少 Desktop 项目的 VM 代码量
- **推荐强度**: `Speculative`（当前 Android 不需要魔术工具，ROI 低）
- **Before / After**:
  - Before: Desktop 独有 600 行 VM
  - After: Shared 共享 VM + IFilePicker 注入

---

### 候选项 6: MainWindowViewModel 手动 DI 替换为简单容器

- **涉及文件**: `MainWindowViewModel.cs` (128 行)
- **问题**: `MainWindowViewModel` 构造函数 (L42-92) 手动创建所有 VM 和服务，手动接线所有事件（6 个事件订阅）。这是一个典型的 **composition root**，但随着功能增长会变得臃肿。
- **方案**: 引入最小 DI 容器（如 `Microsoft.Extensions.DependencyInjection`），在 `App.axaml.cs` 注册服务，构造函数改为注入。
- **收益**:
  - **depth**: composition root 的 interface（构造函数）从 50 行变为 0
  - 但引入 DI 容器增加一个依赖
- **推荐强度**: `Speculative`（当前手动 DI 尚可管理，项目规模不大）

---

## Top Recommendation

**候选项 2: 将 OrganizeSession 深化为完整流程 Facade**

理由：这是改善 locality 最显著的一次深化。当前 "分析→规划→执行" 的完整流程跨 Core 和 Shared 两层散布，WorkbenchViewModel (385 行) 中有约 80 行是分析编排逻辑（创建 Analyzer、调用分析、保存结果、设置 Session）。深化后 ViewModel 瘦身为 thin UI adapter，Core 的 OrganizeSession 成为真正的 deep module，测试也从 VM 层移到 Core 层，可脱离 UI 框架验证完整流程。

其次推荐**候选项 1**（合并 Configuration），因为它是最简单的改动（合并 3 个 shallow 文件），立竿见影。

---

## 摘要

共发现 **6 个架构深化候选项**：2 个 `Strong`，2 个 `Worth exploring`，2 个 `Speculative`。

| # | 候选项 | 强度 | 核心收益 |
|---|--------|------|----------|
| 1 | 合并 Configuration 为 ConfigStore | Strong | locality: 配置持久化集中 |
| 2 | OrganizeSession 深化为流程 Facade | Strong | locality: 完整流程不跨层 |
| 3 | 合并 Storage + Sources 为 IO module | Worth exploring | locality: 文件 I/O 同居 |
| 4 | 统一 Patterns module interface | Worth exploring | depth: interface 收窄 |
| 5 | MagicToolsVM 移入 Shared | Speculative | leverage: 未来复用 |
| 6 | 引入 DI 容器 | Speculative | depth: composition root 瘦身 |
