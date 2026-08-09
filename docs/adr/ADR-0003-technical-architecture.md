# ADR-0003: C# 版技术架构

- 状态：已接受（Accepted）
- 日期：2026-08-08
- 依据：ADR-0001、ADR-0002

## 技术选型

| 项 | 决策 | 备注 |
|----|------|------|
| 运行时 | **.NET 10 LTS** | 支持到 2028 |
| UI 框架 | **Avalonia 12 + MVVM**（CommunityToolkit.Mvvm） | Windows 优先，Mac/Linux 顺带；Android 为远期愿景，不作为当前约束 |
| 图像/元数据 | **Magick.NET-Q16-AnyCPU** | 统一处理 jpg/png/tiff/webp/HEIC 的解码 + EXIF 读取；预览缩略图同库 |
| 视频元数据 | **TagLib#**（或 ImageMagick 读取容器信息，二选一，实现时验证） | 读 mp4/mov 拍摄日期 |
| JSON | System.Text.Json | 数据格式重新设计，不兼容 Python 版 |
| 测试 | xunit + FluentAssertions（Core 库） | GUI 不测 |
| CI/CD | GitHub Actions | build + test + 打包 Release |
| 分发 | **框架依赖小包**（framework-dependent，约 15MB） | 面向开源用户，要求装 .NET 10 运行时 |

## 解决方案结构

```
MediaOrganizer.sln
├── src/
│   ├── MediaOrganizer.Core/          # 纯 .NET 类库，零 UI 依赖（为远期移动端留路）
│   │   ├── Scanning/                 # 目录扫描、扩展名过滤、进度报告
│   │   ├── Extraction/               # IDateExtractor + ExifExtractor / FileNameExtractor
│   │   │                             #   / FileSystemExtractor + WeightedExtractorChain
│   │   ├── Patterns/                 # 模式模型、正则引擎、结构指纹、智能变体生成
│   │   ├── Planning/                 # 归档计划（日期→目标路径、重名消解、FutureDate）
│   │   ├── Execution/                # copy/move 执行、mtime 矫正
│   │   ├── Analysis/                 # 分析编排、detail/unparsed JSON、TXT 报告
│   │   ├── Configuration/            # 配置模型与加载保存
│   │   └── Logging/                  # 内存日志（环形 1000 条）
│   └── MediaOrganizer.Desktop/       # Avalonia MVVM 壳
│       ├── Views/  ViewModels/
│       ├── MagicTools/               # 魔术工具窗口（完整移植）
│       └── ConfigEditor/             # 配置编辑器（4 Tab）
└── tests/
    └── MediaOrganizer.Core.Tests/    # xunit：日期提取、正则、校验、归档计划、重名消解
```

## 关键设计

### 提取器加权链（取代 Python 5 提取器）

```
IDateExtractor { string Name; double Weight; DateTimeOffset? Extract(FileInfo f); }
链按 Weight 降序，取第一个通过 DateRangeValidator 校验的结果
默认：Exif(1.2) → FileName(1.1) → FileSystem(0.8, 默认禁用)
```

### 并行分析

`Parallel.ForEachAsync(files, options, async (f, ct) => ...)`，进度经 `IProgress<T>` 节流上报 UI；无 SQLite、无进程池。

### 失败文件补救闭环

分析失败 → 失败列表预览 → 打开魔术工具加规则 → **重新分析** → 仍失败的批量移动到待处理文件夹。

### 数据格式（重新设计，示例）

```jsonc
// analysis-result.json（取代 detail_config.json / unparsed_files.json）
{
  "version": 1,
  "sourceDir": "...",
  "analyzedAt": "2026-08-08T22:00:00+08:00",
  "parsed":   [ { "path": "...", "date": "2024-01-15", "source": "Exif", "size": 123 } ],
  "unparsed": [ { "path": "...", "reason": "NoValidDate" } ]
}
```

## 实施状态（2026-08-08 v0.1）

已按本 ADR 搭建工程并完成首版实现（`dotnet build` 0 警告，42 个单测全绿，GUI 冒烟启动正常）：

- 实际版本：Avalonia **12.1.1**、Magick.NET-Q16-AnyCPU **14.16.0**、CommunityToolkit.Mvvm 8.4.1、.NET SDK 10.0.300（slnx 解决方案格式）
- 解决方案：`MediaOrganizer.slnx`，目录结构 = 本 ADR §3，全部代码在 `src/`，测试在 `test/`
- Core 已实现：扫描、三条提取器加权链、模式引擎（含时间戳/命名组）、分析编排（Parallel.ForEachAsync + 取消）、归档规划、copy/move 执行（三种同名策略 + mtime 矫正）、TXT 报告、内存日志
- Desktop 已实现：左侧导航 + 工作台（分析/执行）、失败文件（预览 + 批量移动 + 跳转魔术工具）、魔术工具（样本/指纹/正则测试/保存）、设置（4 Tab）、报告、日志
- 已知裁剪（后续迭代）：魔术工具交互式圈选生成正则、文件名着色、视频容器元数据（TagLib# Spike）、英文资源、深色主题资源、GH Actions 流水线
- Avalonia 12 注意：`TextBox.Watermark` 已改名 `PlaceholderText`；`BindingPlugins` 变为 internal（模板清理方法移除）

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| Magick.NET 包体积大（+30MB） | 已接受；框架依赖分发下仍是主要体积来源 |
| 视频拍摄日期字段碎片化（moov/mvhd/©day） | Core 测试集覆盖主流格式；读不到则落入文件名提取 |
| 魔术工具移植工作量大 | 拆为独立窗口，可与主流程并行开发；Core 的正则/指纹/变体逻辑先进类库并有测试 |
| Avalonia Android 支持实验性 | 当前不受其约束；Core 零 UI 依赖保证未来可换壳 |
