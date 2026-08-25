# 代码审查报告 (Code Review)

> 审查日期: 2026-08-21
> 审查范围: 全代码库（Core / Shared / Desktop / Android）
> 审查方法: code-review skill（Standards + Spec 双轴）

---

## Standards 审查

### 代码异味发现

#### 🔴 硬性违规

**S-1. Duplicated Code — FindFreeName 查重名逻辑重复三处**

三个位置有近乎一致的 `FindFreeName` 实现，仅同步/异步和路径分隔符不同：

- `FileOperator.FindFreeNameAsync()` (L160-170)
- `FileOperator.FindFreeLocalPath()` (L172-183)
- `PendingFileMover.FindFreeNameAsync()` (L67-77)

建议：提取为一个通用的静态方法或 `IFileStorage` 扩展方法。

**S-2. Duplicated Code — RoleLabel 函数重复两处**

`MagicToolsViewModel` (L396-408) 和 `MarkableCharVM` (L583-595) 包含一模一样的 `RoleLabel` switch 表达式。

建议：提取为 `MarkRole` 的扩展方法。

**S-3. Duplicated Code — ExistIndex / LevelIndex 映射重复**

`WorkbenchViewModel` 中 `ExistIndex`↔`ExistActionFromIndex` 和 `LevelIndexValue`↔`LevelFromIndex` 是互逆映射，各出现两次（一次构造、一次读取）。

建议：统一为 enum 扩展方法或映射表。

---

#### 🟡 判断性建议

**S-4. Feature Envy — WorkbenchViewModel 过度操作 AppConfig 内部状态**

`WorkbenchViewModel` 大量直接读写 `_config.Paths.*`、`_config.Execute.*` 等字段（如 L213-228、L311-321），而非通过 `AppState` 的业务方法。同样 `SettingsViewModel` 也存在此问题。

建议：`AppState` 提供更高阶的业务 API（如 `SetSourceDir()`），封装 Config 写入 + 变更通知。

**S-5. Data Clumps — 网络配置参数总是一起出现**

`SmbFileStorage` 和 `WebDavFileStorage` 的构造参数 `(address, username, password)` 以及 `StorageFactory.CreateProfileStorage()` 中解密密码并传递的三元组，始终一起出现。

建议：考虑用 `NetworkCredential` 或专用 DTO 封装。

**S-6. Primitive Obsession — 路径用原始 string 表示**

整个代码库中，源路径、输出路径、相对路径、网络地址均为 `string`，语义靠参数名区分。特别是 `IFileStorage` 的 `relativePath` 和 `MediaFile.Path`（可能是本地路径或 `content://` URI）。

建议：引入 `RelativePath`、`SourceIdentifier` 等值对象区分语义。

**S-7. Divergent Change — AppConfig.cs 承担过多职责**

`AppConfig.cs` (122行) 定义了 `FileOperation`、`ExistAction`、`ClassificationLevel`、`NetworkType` 四个枚举，加上 `NetworkProfile`、`ExtractorSetting`、`GeneralConfig`、`PathsConfig`、`ScanConfig`、`ExtractionConfig`、`ExecuteConfig` 七个配置类。任何配置维度的变更都要修改此文件。

建议：按领域拆分到独立文件（如 `Enums.cs`、`NetworkProfile.cs`）。

**S-8. Middle Man — ConfigManager 几乎只是委托**

`ConfigManager` (23行) 仅持有 `JsonOptions` 静态字段并将 `Load`/`Save` 委托给 `JsonFileStore`。

建议：将 `JsonOptions` 移入 `JsonFileStore`，消除 `ConfigManager` 类。但注意 `JsonOptions` 也被 `AnalysisResultStore` 间接使用，需一并处理。

**S-9. Speculative Generality — IFileScanner 接口仅两个实现**

`IFileScanner` 有 `FileScanner`（桌面）和 `AndroidFileScanner`（Android）两个实现。这个抽象在 ADR-0006 中明确有必要（双端复用），属于合理的 seam，**不构成过度抽象**。

（保留但标注为 **justified**。）

**S-10. Message Chains — SmbFileStorage InvokeAsync 嵌套深**

`SmbFileStorage` 中每个操作方法（如 `ExistsAsync`、`CopyFromAsync`）都通过 `InvokeAsync(store => { ... })` 嵌套 lambda，lambda 内部又访问 `store.CreateFile(out handle, ...)` 等 SMBLibrary API。嵌套层级深，阅读不便。

建议：SMBLibrary 的底层 API 确实需要这种结构，但可考虑提取 helper 方法（如 `OpenFile(path, access)`）减少每个操作方法内的重复代码。

**S-11. Shotgun Surgery — 新增提取器需修改多处**

新增一个提取器需要修改：(1) 创建新 `IDateExtractor` 实现类，(2) `ExtractorChain.FromConfig()` 手动添加，(3) `AppConfig.ExtractionConfig.Extractors` 默认列表，(4) `SettingsViewModel.ExtractorSettingVM.DisplayLabel`。四处散布。

建议：考虑自注册或反射发现提取器。但当前仅 3 个提取器，改进 ROI 不高。

---

### C#/.NET 编码标准

**S-12. 空 catch 吞异常**（多处）

以下位置使用空 catch 吞掉异常而不记录日志：
- `ExifExtractor.ExtractFromImage()` L37-39: `catch { return null; }`
- `ExifExtractor.ExtractFromVideo()` L57-59: `catch { }`
- `MagickExifReader.ReadImageExif()` L42-44: `catch { }`
- `FileScanner.Scan()` L43-45: `catch { }`
- `LocalMediaSource.ModifiedTime` L21-23: `catch { return null; }`

虽然注释说明了意图（"损坏文件视为无结果"），但完全吞掉异常会导致排错困难。

建议：至少在 Debug 模式下记录异常信息，或使用 `ILogger` 记录 Trace 级别日志。

**S-13. SmbFileStorage 未实现 IDisposable**

`SmbFileStorage` 持有 `SMB2Client` 和 `ISMBFileStore`（非托管资源连接），以及 `SemaphoreSlim`，但类未实现 `IDisposable`。连接在生命周期结束时不会主动断开。

建议：实现 `IDisposable`，在 `Dispose()` 中断开连接并释放 `SemaphoreSlim`。

**S-14. 主构造函数中缺少参数验证**

多个类使用主构造函数（primary constructor），如 `DateRangeValidator`、`FileSystemExtractor`、`LocalFileStorage`，但未对参数做空值或范围检查。

建议：关键参数（如 `rootPath`、`maxYearsPast`）添加 `ArgumentNullException.ThrowIfNull` 或范围校验。

**S-15. NFR-8 违规：大量中文硬编码字符串**

SRS NFR-8 要求"全部 UI 文本外置资源文件；禁止硬编码可见文本"。但整个 Shared 和 Desktop 项目中，所有可见文本均为硬编码中文字符串：
- `WorkbenchViewModel`: "就绪"、"正在分析…"、"分析完成"等
- `SettingsViewModel`: "已恢复默认配置"、"请填写名称"等
- `FailedFilesViewModel`: "选择文件查看预览"等

这是一个**硬性违规**（与文档化的需求 NFR-8 直接矛盾）。

---

## Spec 审查

### 需求缺失/不完整

**P-1. FR-9.1 菜单系统未实现**（P1）

SRS 要求完整菜单：文件/配置/视图/语言/帮助/报告。当前桌面版采用左侧导航条（NavItem），无菜单栏。功能内容已通过导航页覆盖，但 UI 形态与需求不符。

**P-2. FR-9.4 语言切换未实现**（P1）

国际化框架完全缺失。无 `.resx` 资源文件，无语言切换 UI，所有文本硬编码中文（见 S-15）。

**P-3. FR-6.2 非图片文件预览不完整**

SRS 要求"非图片显示图标与元信息"。当前 `FailedFilesViewModel.LoadPreviewAsync()` 通过 `IImageLoader` 加载缩略图，但非图片文件（如 .avi、.mp4）无图标/元信息展示回退。

**P-4. FR-6.3 双击打开文件**

SRS 要求"双击文件调用系统默认应用打开"。代码中 `OpenWithSystem` 命令存在，但需要确认 View 层是否正确绑定了双击事件。

**P-5. NFR-6 测试覆盖率存疑**

SRS 要求"行覆盖 ≥ 70%"。项目有 12 个测试文件覆盖 Core 关键逻辑，但 Shared ViewModel 层和 Desktop 层完全无测试。未见 CI 覆盖率统计。

**P-6. NFR-9 CI/CD 未实现**

SRS 要求"GitHub Actions 产出 win-x64/osx/linux 框架依赖包，附校验和"。项目中无 `.github/workflows` 目录。

**P-7. FR-10.3 SMB 实现已超越需求**

SRS FR-10.3 说"SMB 走 UNC 路径（Windows 原生）"，但 ADR-0007 已决策改用 SMBLibrary 纯托管客户端。这是**需求文档滞后**，代码实现（SMBLibrary）是正确的更新方向。

**P-8. FR-10.8 WebDAV mtime 矫正未实现**

SRS 要求"WebDAV 用 PROPPATCH"设置修改时间。代码中 `WebDavFileStorage.SetModifiedUtcAsync()` 直接 `return Task.CompletedTask`（静默跳过），注释说明 WebDAVClient 库不支持。符合 SRS 中"服务端不支持则静默跳过"的降级策略。

### 功能范围偏差

**P-9. SRS §5.1 config.json 缺少 language 字段**

SRS 示例中 `general` 包含 `"language": "zh"`，但 `GeneralConfig` 类中无此字段。对应 FR-9.4 国际化未实现。

### 实现与需求不符

**P-10. FR-4.2 分析结果落盘路径变更**

SRS 未指定分析结果文件路径。代码中 `WorkbenchViewModel` 将结果写入 `_dataDir`（`%APPDATA%/MediaOrganizer/`），而 `MagicToolsViewModel.LoadFromFailedFiles()` 却去 `_state.Config.Paths.SourceDir` 下找 `analysis-result.json`（L131）。**两处路径不一致**，后者将始终找不到文件。

---

## 摘要

| 轴 | 发现数 | 硬性违规 | 判断性建议 |
|----|--------|----------|------------|
| **Standards** | 15 | 4（S-1~S-3 重复代码, S-15 硬编码文本） | 11 |
| **Spec** | 10 | — | — |

**Standards 最严重问题**：NFR-8 中文硬编码违反文档化的国际化需求（S-15），以及三处 `FindFreeName` 重复代码（S-1）。

**Spec 最严重问题**：分析结果落盘路径不一致导致魔术工具"从失败文件载入"功能失效（P-10），以及国际化框架完全缺失（P-2）。
