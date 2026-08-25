# 过度工程审计报告 (Ponytail Audit)

> 审查日期: 2026-08-21
> 审查范围: 全代码库
> 审计方法: ponytail-audit skill（仅关注过度工程和复杂度，不查 bug/安全/性能）

---

## 发现列表

按删减收益从大到小排序。格式：`<tag>` `<what to cut>`. `<replacement>`. [path]

---

### 高收益

1. `yagni:` **MagicToolsViewModel.LoadFromFailedFiles() 从源目录找 analysis-result.json**. 此路径与实际落盘路径不一致（结果存在 %APPDATA% 而非源目录），功能实际不可用。删除或修正路径。[Desktop/ViewModels/MagicToolsViewModel.cs:L121-153]

2. `shrink:` **MagicToolsViewModel 597 行，LoadFileNamesFromJsonAsync 手写 JSON 多结构兼容解析 (60行)**. 当前支持 4 种 JSON 格式（analysis-result / files / items / 顶层数组），但项目只产出一种格式。用 `AnalysisResultStore.Load()` 替代手写解析。[Desktop/ViewModels/MagicToolsViewModel.cs:L195-258]

3. `yagni:` **MarkableCharVM.AdvanceRole() 循环切换 9 个角色**. 代码存在但从未被调用（UI 改为 SetRole 按钮式标记后遗留的旧交互方式）。删除死方法。[Desktop/ViewModels/MagicToolsViewModel.cs:L565-577]

4. `shrink:` **RoleLabel switch 表达式在两个类中重复** (MagicToolsViewModel L396-408 + MarkableCharVM L583-595, 共 26 行). 提取为 `MarkRole` 扩展方法，净减约 15 行。[Desktop/ViewModels/MagicToolsViewModel.cs]

5. `yagni:` **ConfigManager 类 (23 行) — 只持有 JsonOptions + 委托两行方法**. 将 JsonOptions 移入 JsonFileStore，删除 ConfigManager，调用点直接用 JsonFileStore。[Core/Configuration/ConfigManager.cs]

---

### 中等收益

6. `yagni:` **IFileScanner 接口仅两个实现（FileScanner + AndroidFileScanner），且 Android 端处于早期骨架**. 抽象合理（ADR-0006 明确要求双端注入），但如果 Android 端取消，此接口可内联。**保留但标注为 justified by ADR-0006**。

7. `yagni:` **ICredentialCrypto 接口仅两个实现，且 CredentialCrypto 静态门面已通过 `Current` 属性做策略切换**. 两层抽象（接口 + 静态门面 + `Current` 属性注入）稍显繁琐。可简化为仅保留接口或仅保留静态门面。**保留但标注为 justified by ADR-0005**。

8. `shrink:` **FindFreeName 逻辑重复三次** (FileOperator L160-170, FileOperator L172-183, PendingFileMover L67-77). 提取公共方法，净减约 20 行。[Core/Execution/]

9. `shrink:` **ExistIndex / LevelIndex / ExistActionFromIndex / LevelFromIndex 四组映射互逆重复** (WorkbenchViewModel). 合并为 enum 扩展方法。[Shared/ViewModels/WorkbenchViewModel.cs]

10. `yagni:` **AppLogger.Capacity 属性 public 可读但无处使用**. Capacity 在构造时设置后仅在 `Log()` 内部读取。改为 private readonly 字段。[Core/Logging/AppLogger.cs:L16]

11. `shrink:` **SettingsViewModel.ResetConfigToDefaults() 手写默认值** (L200-238, 38 行). 与 AppConfig 各子类的字段初始化器重复。改为 `config = new AppConfig()`，然后只覆盖非默认字段。[Shared/ViewModels/SettingsViewModel.cs]

---

### 低收益

12. `yagni:` **MemoryMediaSource.Delete() 抛 NotSupportedException**. 仅用于连接测试探针，Delete 永远不会被调用。可改为空实现或保留——收益极小。[Core/Sources/MemoryMediaSource.cs:L13]

13. `yagni:` **PatternInferrer.GenerateVariantRegex() 多变体生成**. 功能存在且被 MagicToolsVM 调用，但生成的正则质量不高（逐字符比较，无结构感知）。是否真正有用户价值存疑，但 SRS FR-7.4 明确要求此功能。**保留——SRS 要求**。

14. `shrink:` **ViewModelBase 空基类** (Shared/ViewModels/ViewModelBase.cs). 如果仅继承 `ObservableObject` 且无自定义行为，可直接 `using CommunityToolkit.Mvvm.ComponentModel` 省去继承层。但基类可能为未来扩展预留。保留。

15. `yagni:` **LogsViewModel** (Desktop). 未能阅读该文件，但日志功能是 SRS FR-9.2 明确要求，保留。

16. `shrink:` **SmbFileStorage.InvokeAsync 中 `when (ex is ObjectDisposedException)` 可简化为 `catch (ObjectDisposedException)`**. 单行变化，收益极小。[Core/Storage/SmbFileStorage.cs:L108]

---

## 依赖审查

| 依赖 | 审计结论 |
|------|----------|
| **Magick.NET-Q16-AnyCPU** | 必需（EXIF/HEIC 读取），无 stdlib 替代 |
| **TagLibSharp** | 必需（视频元数据），无 stdlib 替代 |
| **SMBLibrary** | 必需（纯托管 SMB 客户端，ADR-0007），.NET 无内置 SMB 客户端 API |
| **WebDAVClient** | 必需（WebDAV 协议），.NET HttpClient 能做但需大量手写；库覆盖 PROPFIND/MKCOL 等 |
| **System.Security.Cryptography.ProtectedData** | 必需（DPAPI），仅 Windows |
| **Xamarin.AndroidX.DocumentFile** | 必需（Android SAF），仅 Android |
| **CommunityToolkit.Mvvm** | 必需（MVVM 框架），替代方案是手写，不值得 |
| **Avalonia** 系列 | 必需（UI 框架） |

**结论：无可删除的依赖。** 每个依赖都有明确用途且无 stdlib/native 替代。

---

## 详细说明

### 关于 ConfigManager（发现 #5）

`ConfigManager` 全部代码：

```csharp
public static class ConfigManager
{
    public static readonly JsonSerializerOptions JsonOptions = new() { ... };
    public static AppConfig Load(string path) => JsonFileStore.Load<AppConfig>(path) ?? new AppConfig();
    public static void Save(string path, AppConfig config) => JsonFileStore.Save(path, config);
}
```

这是一个教科书式的 **Middle Man**：两个方法直接委托给 `JsonFileStore`，唯一的"增值"是 `?? new AppConfig()` 空值兜底和 `JsonOptions` 静态字段。将 `JsonOptions` 移入 `JsonFileStore`（它本身就已在引用 `ConfigManager.JsonOptions`），Load 空值兜底放入调用点（`AppState.Load`），`ConfigManager` 即可删除。

### 关于 FindFreeName 重复（发现 #8）

三处实现：

| 位置 | 同步/异步 | 路径分隔符 | 参数 |
|------|-----------|------------|------|
| `FileOperator.FindFreeNameAsync` | async | `/` | `relativeTarget` |
| `FileOperator.FindFreeLocalPath` | sync | `Path.Combine` | `targetPath` |
| `PendingFileMover.FindFreeNameAsync` | async | `Path.GetExtension` | `name` |

核心逻辑相同：`stem_n.ext`，n 从 1 递增直到不存在。可提取为：
```csharp
static async Task<string> FindFreeAsync(string stem, string ext, Func<string, Task<bool>> exists)
```

---

## 净效果

```
net: -~120 lines, -0 deps possible.
```

项目整体 **较为精简**，没有严重的过度工程问题。主要收益来自重复代码合并（~40 行）、死代码清理（~30 行）、以及 ConfigManager 删除（~23 行）。依赖全部必要，无可删减项。

项目架构在 ADR 的指导下做出了合理的抽象决策（IFileScanner、IMediaSource、IFileStorage、ICredentialCrypto、IExifReader），每个接口都有 ≥2 个实现或明确的双端复用需求。**这是一个偏精益的代码库。**
