# 软件需求规格说明书（SRS）

**项目名称**：MediaOrganizer（媒体文件整理器 · C# 版）
**版本**：1.1（新增网络输出目标）
**日期**：2026-08-12
**依据文档**：ADR-0001（迁移决策）、ADR-0002（功能范围）、ADR-0003（技术架构）、ADR-0004（网络输出目标）、docs/glossary.md（领域词汇表）

---

## 1. 引言

### 1.1 目的

本文档定义 MediaOrganizer C# 版的全部功能性与非功能性需求，作为设计、开发、测试与验收的基准。本项目为既有 Python 版（PySide6）的迁移与精简重构，面向开源发布。

### 1.2 范围

MediaOrganizer 扫描用户指定的源目录，从照片/视频文件中提取拍摄日期，按"年/月/日"分级目录结构将文件复制或移动到输出目录，并提供失败文件管理与可视化正则规则编辑（魔术工具）能力。

**不包含**：照片浏览/相册管理、图片编辑、云端同步、重复文件去重（MD5 指纹）、文件元数据写入。

### 1.3 术语

见 docs/glossary.md。关键术语：分析、执行、提取器、加权链、文件名模式、结构指纹、失败文件、待处理文件夹。

### 1.4 参考资料

- `功能需求要点.md`（Python 版功能基准）
- `docs/adr/ADR-0001~0003`
- 界面原型：`docs/界面原型设计/index.html`

---

## 2. 总体描述

### 2.1 产品定位

开源桌面工具（MIT/Apache 许可证待定）。首发中文界面，文本资源外置，预留英文。

### 2.2 运行环境

| 项 | 要求 |
|----|------|
| 操作系统 | Windows 10 1809+（主目标）；macOS 12+、主流 Linux 桌面（顺带支持） |
| 运行时 | .NET 10（框架依赖分发，用户需安装运行时） |
| 硬件 | 无特殊要求；几万张文件规模下 4GB 内存足够 |

### 2.3 用户画像

- 主要用户：有大量手机/相机导出照片需要按日期归档的个人用户
- 次要用户：愿意为怪异文件名编写/分享正则规则的进阶开源用户

### 2.4 设计约束

1. Core 类库（MediaOrganizer.Core）不得依赖任何 UI 框架（ADR-0003，为远期移动端留路）
2. 数据格式重新设计，不与 Python 版兼容（ADR-0002），但 Python 版 patterns.json 的规则内容应可人工迁移
3. 不引入 SQLite 依赖；日志仅内存 + TXT 报告
4. UI 使用 Avalonia 12 + MVVM（CommunityToolkit.Mvvm）

### 2.5 假设与依赖

- 图像/HEIC 解码依赖 Magick.NET-Q16-AnyCPU
- 视频拍摄日期读取依赖 TagLib# 或 Magick.NET（实现 Spike 验证后定，见风险 R-2）

---

## 3. 功能需求

优先级：**P0** = 首发必须；**P1** = 首发应有；**P2** = 可延后版本。

### FR-1 文件扫描（P0）

| 编号 | 需求 |
|------|------|
| FR-1.1 | 递归扫描源目录下所有文件 |
| FR-1.2 | 按配置的扩展名白名单过滤（jpg/jpeg/png/tiff/tif/bmp/webp/heic/heif/mp4/mov/avi/mkv/wmv/flv/webm/m4v/mpg/mpeg/3gp/3g2） |
| FR-1.3 | 可选"扫描所有文件"模式，忽略扩展名限制 |
| FR-1.4 | 扫描与分析过程通过 IProgress 节流上报进度（可配置间隔），UI 不卡顿 |

### FR-2 日期提取（P0，核心）

| 编号 | 需求 |
|------|------|
| FR-2.1 | 提供 3 个提取器：**ExifExtractor**（Magick.NET 读 EXIF 的 DateTimeOriginal/DateTimeDigitized/DateTime，支持 HEIC）、**FileNameExtractor**（正则模式）、**FileSystemExtractor**（mtime 兜底，默认禁用） |
| FR-2.2 | 提取器按权重降序组成加权链，取第一个通过校验的结果。默认权重：Exif 1.2 > FileName 1.1 > FileSystem 0.8 |
| FR-2.3 | 视频文件读取容器元数据中的拍摄日期（mp4/mov 优先） |
| FR-2.4 | 日期统一校验：1970-01-01 ~ 2100-12-31；不早于（当前年份 − max_years_past，默认 30）；不晚于（当前日期 + future_date_buffer_days，默认 0）。校验失败视为该提取器无结果，继续下一个 |
| FR-2.5 | 每个提取器可独立启用/禁用、调整权重（配置编辑器） |

### FR-3 文件名模式引擎（P0）

| 编号 | 需求 |
|------|------|
| FR-3.1 | 模式模型：名称、正则、group_mapping（year/month/day/hour/minute/second → 捕获组号）、ignored_groups、timestamp_length（10/13/16 位时间戳）、enabled、weight |
| FR-3.2 | 预置常见模式：通用日期时间、Unix 时间戳（10/13/16 位）、微信 mm_export、小红书、OPPO、yyyy-MM-dd / yyyy_MM_dd / yyyyMMdd / yyyyMMddHHmmss 等 |
| FR-3.3 | 快速年份预扫描：文件名中不存在合理 4 位年份（且无时间戳模式标记）时跳过全部正则 |
| FR-3.4 | 结构指纹：文件名字符分类为 D/L/S 生成指纹串，用于列表排序聚类 |
| FR-3.5 | 用户可通过魔术工具新增/编辑/删除/启停模式，持久化到 patterns.json（新格式） |

### FR-4 分析阶段（P0）

| 编号 | 需求 |
|------|------|
| FR-4.1 | 一键分析：扫描 → 并行提取（Parallel.ForEachAsync，最大并发数可配，默认 CPU 核心数）→ 产出结果 |
| FR-4.2 | 产出 `analysis-result.json`（结构见 §5.2）：parsed（含日期、来源、大小）与 unparsed（含失败原因） |
| FR-4.3 | 产出 TXT 分析报告：源/输出目录、总数/成功/失败/成功率、按日期来源统计、按年/月分布、失败文件清单 |
| FR-4.4 | 分析可在 UI 上查看统计摘要；分析报告可从"报告"菜单查看/另存 |
| FR-4.5 | 取消支持：分析过程中可取消（CancellationToken），已得进度可保留展示 |

### FR-5 执行阶段（P0）

| 编号 | 需求 |
|------|------|
| FR-5.1 | 基于分析结果按分级目录归档：year（2024/）、month（2024/01/）、day（2024/01/15/，默认） |
| FR-5.2 | 操作类型：copy（默认）/ move。不提供 link |
| FR-5.3 | 同名处理：skip（默认）/ overwrite / rename（追加 `_1`、`_2`…） |
| FR-5.4 | 未来日期文件统一归入 `FutureDate/` |
| FR-5.5 | 可选 mtime 矫正：将目标文件修改时间设为提取日期 |
| FR-5.6 | 执行前展示计划摘要（涉及文件数、目标根目录、操作类型），用户确认后执行 |
| FR-5.7 | 执行进度、可取消；执行完成输出结果摘要（成功/跳过/覆盖/重命名/失败计数） |

### FR-6 失败文件管理（P0）

| 编号 | 需求 |
|------|------|
| FR-6.1 | 分析后展示失败文件列表（文件名、路径、原因），按结构指纹智能排序聚类 |
| FR-6.2 | 选中文件显示图片预览（jpg/png/tiff/bmp/webp/heic）；非图片显示图标与元信息 |
| FR-6.3 | 双击文件调用系统默认应用打开 |
| FR-6.4 | 一键打开魔术工具（带入当前失败文件名样本）以新增规则 |
| FR-6.5 | 批量移动失败文件到用户指定的"待处理文件夹"（带确认与进度） |

### FR-7 魔术工具（P1，完整移植）

| 编号 | 需求 |
|------|------|
| FR-7.1 | 从源目录 / 分析结果（成功或失败）加载文件名样本列表 |
| FR-7.2 | 字符着色视图：数字粉色、字母绿色、符号蓝色；显示结构指纹 |
| FR-7.3 | 交互式选择：用户在文件名上圈选片段并标记为 年/月/日/时/分/秒/时间戳/忽略，自动据此生成正则与 group_mapping |
| FR-7.4 | 多变体智能生成：对比多个同指纹文件名，差异部分生成 `\d{n}` 或通配，生成覆盖全部样本的正则 |
| FR-7.5 | 实时测试：输入或生成正则后即时显示对样本列表的命中/解析结果 |
| FR-7.6 | 保存模式到 patterns.json（名称、正则、group_mapping、ignored_groups、timestamp_length、权重、启用状态） |
| FR-7.7 | 测试与保存区域的样本列表保持固定高度，使用局部滚动条浏览，防止列表高度过度延伸 |

### FR-8 配置编辑器（P1）

| 编号 | 需求 |
|------|------|
| FR-8.1 | Tab 1 提取器：启用/禁用、权重调整、参数（max_years_past、future_date_buffer_days） |
| FR-8.2 | Tab 2 文件名模式：模式列表查看/启停/删除，跳转魔术工具 |
| FR-8.3 | Tab 3 系统：网络位置管理、扩展名白名单、扫描所有文件、扫描/执行并发数、进度间隔 |
| FR-8.4 | Tab 4 GUI：主题（亮/暗）、窗口尺寸、预览图尺寸 |
| FR-8.5 | 配置保存到 `%APPDATA%\MediaOrganizer\config.json`；所有改动实时保存，无需手动点击保存；提供「初始化默认配置」按钮一键恢复出厂设置并保留网络位置 |
| FR-8.6 | 源/输出/待处理目录、分级目录、同名策略、mtime 矫正由整理工作台统一设置，避免与系统 Tab 重复 |

### FR-9 应用外壳（P1）

| 编号 | 需求 |
|------|------|
| FR-9.1 | 菜单：文件（打开/保存/加载配置、退出）｜配置（配置编辑器、魔术工具）｜视图（日志、主题切换）｜语言｜帮助（使用说明、关于）｜报告（分析报告） |
| FR-9.2 | 日志视图：内存环形日志（上限 1000 条），可刷新/清空 |
| FR-9.3 | 状态栏：聚合当前页面操作反馈（进度、提示、计数），不显示版本/框架等固定信息 |
| FR-9.4 | 语言切换实时刷新全部 UI 文本；首发仅提供 zh 资源，en 留空位 |

### FR-10 网络输出目标（P0，v1.0 新增，ADR-0004）

| 编号 | 需求 |
|------|------|
| FR-10.1 | 输出目录支持本地路径与网络位置两类目标；源目录保持本地 |
| FR-10.2 | 网络位置以"连接配置"管理：名称、协议（SMB / WebDAV）、地址（`\\server\share` 或 WebDAV URL）、用户名、密码 |
| FR-10.3 | SMB 走 UNC 路径（Windows 原生）；WebDAV 走协议客户端，全平台可用 |
| FR-10.4 | 连接配置提供"测试连接"即时验证（认证 + 写权限探测） |
| FR-10.5 | 网络目标下仅提供 copy，move 选项禁用并给出原因提示 |
| FR-10.6 | 分块流式拷贝（8MB 缓冲），逐文件 + 总体双进度；网络中断自动重试 3 次（指数退避） |
| FR-10.7 | 传输采用临时名（`<name>.mo-tmp`）+ 完成后改名，杜绝半成品；完成校验 = 大小一致 |
| FR-10.8 | mtime 矫正兼容网络目标：SMB 用 SetLastWriteTime；WebDAV 用 PROPPATCH（服务端不支持则静默跳过） |
| FR-10.9 | 明确不支持 FTP（ADR-0004）；SMB 在 macOS/Linux 需用户自行挂载（README 注明） |

### FR-11 凭据管理（P0，v1.0 新增）

| 编号 | 需求 |
|------|------|
| FR-11.1 | 凭据随连接配置保存在 config.json |
| FR-11.2 | Windows：密码使用 DPAPI（CurrentUser 域）加密存储 |
| FR-11.3 | macOS/Linux：首版 Base64 降级存储并在 UI 与 README 明确警告；后续版本接入系统密钥环 |
| FR-11.4 | 日志与报告中严禁出现密码明文 |

---

## 4. 界面需求

以 `docs/界面原型设计/index.html` 为准（Avalonia 12 Fluent 风格，不沿用 Qt 布局）。要点：

- 主窗口单窗口三区块：顶部目录与操作区、中部进度与统计、底部失败文件列表 + 预览
- 整理工作台目录区统一设置源/输出（含本地与网络目标）/待处理三类目录
- 分析→执行为主流程向导式引导，降低开源用户学习成本
- 魔术工具、配置编辑器为独立窗口
- 输出目录支持本地/网络位置切换；网络位置配置在设置-系统 Tab 内联管理（列表 + 测试/编辑/删除）
- 设置-系统 Tab 含"网络位置"管理卡片与扫描/执行参数，不含目录与执行策略（已移至工作台）
- 日志页的"清空"按钮与分析报告页的"另存为"按钮保持统一美术风格

---

## 5. 数据与接口

### 5.1 config.json（新格式，示例节选）

```jsonc
{
  "version": 1,
  "general": { "language": "zh", "theme": "light", "windowSize": "1280x760", "previewSize": 320 },
  "paths": { "sourceDir": "", "outputDir": "", "pendingDir": "" },
  "scan": { "supportedFormats": ["jpg","jpeg","png","heic","mp4","mov","..."], "scanAllFiles": false, "progressInterval": 10, "maxDegreeOfParallelism": 0 },
  "extraction": {
    "maxYearsPast": 30, "futureDateBufferDays": 0,
    "extractors": [
      { "name": "Exif", "enabled": true, "weight": 1.2 },
      { "name": "FileName", "enabled": true, "weight": 1.1 },
      { "name": "FileSystem", "enabled": false, "weight": 0.8 }
    ]
  },
  "execute": { "operation": "copy", "existAction": "skip", "classificationLevel": "day", "fixMtime": false },
  "networkProfiles": [
    {
      "name": "家里的 NAS",
      "type": "Smb",                  // Smb | WebDav
      "address": "\\\\192.168.1.10\\photos",   // Smb: UNC；WebDav: https://dav.example.com/photos
      "username": "dioha",
      "password": "DPAPI:...",        // Windows DPAPI 加密；macOS/Linux 首版 "B64:..."（降级，见 FR-11.3）
      "lastVerifiedAt": "2026-08-09T10:00:00+08:00"
    }
  ]
}
```

### 5.2 analysis-result.json

```jsonc
{
  "version": 1,
  "sourceDir": "D:\\Photos\\import",
  "analyzedAt": "2026-08-08T22:00:00+08:00",
  "parsed": [
    { "path": "D:\\Photos\\import\\IMG_0001.HEIC", "date": "2024-01-15T14:30:00", "source": "Exif", "size": 2456789 }
  ],
  "unparsed": [
    { "path": "D:\\Photos\\import\\scan001.tif", "reason": "NoValidDate" }
  ]
}
```

### 5.3 patterns.json

```jsonc
{
  "version": 1,
  "patterns": [
    { "name": "微信导出", "pattern": "mm_export(\\d{13})", "groupMapping": { "timestamp": 1 },
      "ignoredGroups": [], "timestampLength": 13, "enabled": true, "weight": 1.0, "builtin": true }
  ]
}
```

### 5.4 TXT 分析报告

沿用 Python 版报告的信息结构（统计、来源分布、年月分布、失败清单），格式可微调。

---

## 6. 非功能需求

| 编号 | 类别 | 需求 |
|------|------|------|
| NFR-1 | 性能 | 几万张规模：分析吞吐 ≥ 200 文件/秒（SSD，不含 HEIC 重解码）；UI 全程无阻塞 |
| NFR-2 | 性能 | 内存峰值 ≤ 500MB（内存日志环形 1000 条；不缓存原图，只缓存缩略图） |
| NFR-3 | 可靠性 | 单个文件解析失败不得中断整体分析；所有 IO 操作有异常捕获与日志 |
| NFR-4 | 可靠性 | 执行阶段遇错（占用、权限）记录并继续，结束汇总报告 |
| NFR-5 | 可移植 | Core 零 UI 依赖；路径处理统一 API，禁止拼接字符串路径 |
| NFR-6 | 可测试 | Core 关键逻辑（提取、校验、模式引擎、归档计划、重名消解）xunit 覆盖，行覆盖 ≥ 70% |
| NFR-7 | 开源合规 | 第三方依赖许可证清单随 Release 发布（Magick.NET Apache-2.0 等） |
| NFR-8 | 国际化 | 全部 UI 文本外置资源文件；禁止硬编码可见文本 |
| NFR-9 | 分发 | GitHub Actions 产出 win-x64 / osx / linux 框架依赖包，附校验和 |
| NFR-10 | 安全 | 密码不得明文落盘（Windows）；任何平台日志/报告不得出现凭据；连接测试失败不泄露密码细节 |
| NFR-11 | 网络可靠性 | 网络传输单文件失败重试 ≤ 3 次且退避间隔 ≤ 8s；断线不导致 UI 卡死；WebDAV 请求超时默认 30s 可配 |

---

## 7. 验收标准（首发 v1.0）

1. 对含 ≥ 1000 个混合格式文件的测试目录完成分析，成功率与 Python 版同目录结果偏差 ≤ 2%（同规则集下）
2. copy 模式执行后，输出目录结构与分析日期一致；同名 rename 策略正确追加序号
3. 魔术工具可新增一个自定义正则模式并立即用于重新分析，原失败文件被成功解析
4. 分析/执行过程可取消且无残留部分写坏的状态文件
5. Core 测试与 CI 全部通过；Release 页面可下载三平台包
6. 新增 SMB/WebDAV 连接配置 → 测试连接通过 → 归档到网络目标，结果与本地一致；断网重试后错误清单正确
7. 网络目标下 move 选项禁用；密码在 config.json 中为密文（Windows）

---

## 8. 风险

| 编号 | 风险 | 缓解 |
|------|------|------|
| R-1 | Magick.NET 使包体积 +约 30MB | 已接受（ADR-0003） |
| R-2 | 视频拍摄日期字段碎片化 | 开发早期 Spike 验证 TagLib#/Magick.NET 覆盖率，不足则视频仅靠文件名 |
| R-3 | 魔术工具移植工作量大 | 独立窗口并行开发；引擎逻辑在 Core 先行测试 |
| R-4 | Avalonia 12 新版 API 变动 | 脚手架阶段锁定版本并验证控件清单 |
| R-5 | WebDAV 服务端实现差异（PROPPATCH/分块/超时） | 用真实 NAS/坚果云做兼容性 Spike；失败时降级为本地归档并提示 |
| R-6 | SMB UNC 仅 Windows 可用 | v1.0 文档注明 macOS/Linux 需外部挂载；后续评估 SMBLibrary 纯托管实现 |
| R-7 | macOS/Linux 凭据首版降级存储 | UI 警告 + README 注明；下一版接入系统密钥环 |
