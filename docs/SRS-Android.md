# 软件需求规格说明书（SRS）· Android 版

**项目名称**：MediaOrganizer（媒体文件整理器 · Android 版）
**版本**：1.0-android
**日期**：2026-08-13
**依据文档**：ADR-0003（技术架构）、ADR-0004（网络输出目标）、ADR-0005（Android 分支）、ADR-0006（源端抽象与共享层重构）、桌面版 SRS、docs/glossary.md

---

## 1. 引言

### 1.1 目的

本文档定义 MediaOrganizer Android 版的全部功能性与非功能性需求，作为设计、开发、测试与验收的基准。Android 版复用桌面版 Core 库的业务逻辑，针对移动端交互模式和 Android 平台特性做适配。

### 1.2 范围

MediaOrganizer Android 版访问用户指定的源目录（全局存储权限直读真实路径，第三方文档提供方回退 SAF），从照片/视频文件中提取拍摄日期，按"年/月/日"分级目录结构将文件复制或移动到目标目录（本地 SAF 目录或 WebDAV 网络位置），并提供失败文件管理与分析报告能力。

**不包含**（相对桌面版的裁剪）：
- 魔术工具（触屏不适合圈选交互）
- 日志查看器
- 缩略图预览（后续迭代）

（2026-08-15 修订：SMB 网络存储自"不包含"移除——ADR-0007 激活 SMBLibrary，Android 支持 SMB 输出目标）

### 1.3 术语

见 docs/glossary.md。Android 专属术语：SAF、树 URI、ContentResolver、DocumentFile、Scoped Storage、AndroidSafStorage、AndroidFileScanner、Android Keystore、ICredentialCrypto、IExifReader、ExifInterface、应用专有目录、抽屉导航。

### 1.4 参考资料

- 桌面版 SRS（docs/SRS.md）
- ADR-0005（Android 分支技术栈与架构）
- 界面原型：`docs/android-prototype/index.html`

---

## 2. 总体描述

### 2.1 产品定位

开源 Android 工具（MIT 许可证）。首发中文界面，文本资源外置。APK 侧载分发（GitHub Release），不上架 Google Play。

### 2.2 运行环境

| 项 | 要求 |
|----|------|
| 操作系统 | Android 13（API 33）及以上 |
| 架构 | arm64-v8a（主）；armeabi-v7a / x86_64（模拟器） |
| 运行时 | .NET 10 for Android（自包含 APK） |
| 存储 | 应用专有目录无需权限；本地源/目标经"所有文件访问"（MANAGE_EXTERNAL_STORAGE）直读真实路径，第三方文档提供方回退 SAF |

### 2.3 用户画像

- 主要用户：手机拍照/截图积累大量、想按日期归档到 NAS（WebDAV）或本地目录的用户
- 次要用户：从微信/小红书等 App 导出文件名混乱、需要文件名模式解析的用户

### 2.4 设计约束

1. Core 类库多目标 `net10.0;net10.0-android`，同一份代码（ADR-0005）
2. 存储访问优先申请 `MANAGE_EXTERNAL_STORAGE` 全文件权限走真实路径（APK 侧载分发，不受 Play 政策约束）；SAF 仅作为第三方文档提供方回退
3. 凭据加密使用 Android Keystore（非 DPAPI/Base64）
4. 图片 EXIF 读取使用 `Android.Media.ExifInterface`（非 Magick.NET）
5. UI 使用 Avalonia 12 Android + MVVM，ViewModel 复用桌面版，View 全新重写
6. 导航模式：单页 + 抽屉（非桌面版的左侧 Tab 导航）

### 2.5 假设与依赖

- Core 库已完成跨平台准备（Magick.NET 条件排除、`#if ANDROID` 预留、DPAPI 守卫）
- 里程碑 0 重构（ADR-0006）已完成：IMediaSource 源端抽象、提取/执行链路走流、MediaOrganizer.Shared 抽离
- TagLibSharp（视频元数据）纯托管，经 StreamFileAbstraction 走流后双端共享（ADR-0006 决策 2）
- WebDAVClient 纯托管，Android 直接可用

---

## 3. 功能需求

优先级：**P0** = 首发必须；**P1** = 首发应有；**P2** = 可延后版本。

### FR-A1 文件扫描（P0）

| 编号 | 需求 |
|------|------|
| FR-A1.1 | 用户经系统目录选择器（`ACTION_OPEN_DOCUMENT_TREE`）选取源目录；externalstorage 提供方确定性转换为真实路径存储，其余回退树 URI |
| FR-A1.2 | 递归遍历 DocumentFile 树，按配置的扩展名白名单过滤（与桌面版一致的格式列表） |
| FR-A1.3 | 可选"扫描所有文件"模式，忽略扩展名限制 |
| FR-A1.4 | 扫描与分析过程通过 IProgress 节流上报进度，UI 不卡顿（主线程不阻塞） |
| FR-A1.5 | 树 URI 授权持久化（`takePersistableUriPermission`），应用重启后无需重新选取 |

### FR-A2 日期提取（P0，核心）

| 编号 | 需求 |
|------|------|
| FR-A2.1 | 提供 3 个提取器：**ExifExtractor**（Android 侧由 ExifInterface 实现图片 EXIF；视频由 TagLib# 读容器元数据）、**FileNameExtractor**（正则模式，与桌面版共享 PatternEngine）、**FileSystemExtractor**（mtime 兜底，默认禁用） |
| FR-A2.2 | 提取器按权重降序组成加权链，取第一个通过校验的结果。默认权重：Exif 1.2 > FileName 1.1 > FileSystem 0.8 |
| FR-A2.3 | 视频文件读取容器元数据中的拍摄日期（mp4/mov 优先），使用 TagLib# 经 StreamFileAbstraction 从 IMediaSource 流读取（与桌面版共享同一份解析代码，ADR-0006） |
| FR-A2.4 | 日期统一校验：1970-01-01 ~ 2100-12-31；不早于（当前年份 − max_years_past，默认 30）；不晚于（当前日期 + future_date_buffer_days，默认 0）。校验失败视为该提取器无结果，继续下一个 |
| FR-A2.5 | 每个提取器可独立启用/禁用、调整权重（设置页） |
| FR-A2.6 | 图片 EXIF 读取支持 jpg/jpeg/png/webp/heic/heif；tiff/bmp 不保证（ExifInterface 限制，落入文件名提取器兜底） |

### FR-A3 文件名模式引擎（P0）

| 编号 | 需求 |
|------|------|
| FR-A3.1 | 与桌面版完全共享 PatternEngine / PatternInferrer / StructureFingerprint（Core 库同一份代码） |
| FR-A3.2 | 预置常见模式（与桌面版一致）：通用日期时间、Unix 时间戳、微信 mm_export、小红书、OPPO 等 |
| FR-A3.3 | patterns.json 存储于应用专有目录；可在设置页查看/启停/删除模式 |
| FR-A3.4 | **不提供**魔术工具（正则编辑器）；用户如需自定义模式，在桌面版编辑后把 patterns.json 复制到 Android 设备 |

### FR-A4 分析阶段（P0）

| 编号 | 需求 |
|------|------|
| FR-A4.1 | 一键分析：目录扫描（真实路径直读优先，SAF 回退）→ 并行提取（Parallel.ForEachAsync，最大并发数可配，默认 CPU 核心数）→ 产出结果 |
| FR-A4.2 | 产出 `analysis-result.json`（结构与桌面版一致）：parsed（含日期、来源、大小）与 unparsed（含失败原因）；落盘到应用专有目录（ADR-0006 决策 6），不写 SAF 源目录 |
| FR-A4.3 | 产出 TXT 分析报告：源/输出目录、总数/成功/失败/成功率、按日期来源统计、按年/月分布、失败文件清单 |
| FR-A4.4 | 分析可在 UI 上查看统计摘要（总数/成功/失败/成功率、按来源/年份分布） |
| FR-A4.5 | 取消支持：分析过程中可取消（CancellationToken），已得进度可保留展示 |

### FR-A5 执行阶段（P0）

| 编号 | 需求 |
|------|------|
| FR-A5.1 | 基于分析结果按分级目录归档：year（2024/）、month（2024/01/）、day（2024/01/15/，默认） |
| FR-A5.2 | 操作类型：copy（默认）/ move。网络目标（WebDAV）下仅 copy |
| FR-A5.3 | 同名处理：skip（默认）/ overwrite / rename（追加 `_1`、`_2`…） |
| FR-A5.4 | 未来日期文件统一归入 `FutureDate/` |
| FR-A5.5 | 可选 mtime 矫正：将目标文件修改时间设为提取日期（WebDAV 不支持时静默跳过） |
| FR-A5.6 | 执行前展示计划摘要（涉及文件数、目标根目录、操作类型），用户确认后执行 |
| FR-A5.7 | 执行进度、可取消；执行完成输出结果摘要（成功/跳过/覆盖/重命名/失败计数） |
| FR-A5.8 | 本地目标：真实路径 System.IO 写入（content: 回退 ContentResolver）；网络 WebDAV 目标：流式读源 → 流式上传 |
| FR-A5.9 | 临时名传输（`<name>.mo-tmp`）+ 完成后改名 + 大小校验，与桌面版一致 |

### FR-A6 失败文件管理（P0）

| 编号 | 需求 |
|------|------|
| FR-A6.1 | 分析后展示失败文件列表（文件名、路径、原因），按结构指纹智能排序聚类 |
| FR-A6.2 | **不提供**图片预览（后续迭代） |
| FR-A6.3 | 点击文件可用系统默认应用打开（通过 URI Intent） |
| FR-A6.4 | **不提供**跳转魔术工具（Android 无魔术工具） |
| FR-A6.5 | 批量移动失败文件到用户指定的"待处理目录"（SAF 选取，带确认与进度）；走 Core 共享服务 PendingFileMover，SAF→SAF 移动语义 = 流式复制 + 删除源（ADR-0006 决策 5） |

### FR-A7 应用外壳（P1）

| 编号 | 需求 |
|------|------|
| FR-A7.1 | 抽屉导航：汉堡菜单切换页面（整理工作台 / 失败文件 / 分析报告 / 设置） |
| FR-A7.2 | 状态栏：聚合当前页面操作反馈（进度、提示、计数），底部固定显示 |
| FR-A7.3 | 返回键：抽屉打开时关闭抽屉，否则退出应用 |
| FR-A7.4 | 语言切换实时刷新全部 UI 文本；首发仅提供 zh 资源 |

### FR-A8 设置（P1）

| 编号 | 需求 |
|------|------|
| FR-A8.1 | 提取器配置：启用/禁用、权重调整、参数（max_years_past、future_date_buffer_days） |
| FR-A8.2 | 文件名模式：模式列表查看/启停/删除（不含魔术工具编辑） |
| FR-A8.3 | 网络位置管理：WebDAV 连接配置列表 + 测试连接 + 增删改 |
| FR-A8.4 | 扫描参数：扩展名白名单、扫描所有文件、扫描/执行并发数 |
| FR-A8.5 | 配置保存到应用专有目录 `config.json`；所有改动实时保存 |
| FR-A8.6 | 源/输出/待处理目录、分级目录、同名策略、mtime 矫正由整理工作台统一设置 |

### FR-A9 网络输出目标（P0，ADR-0005）

| 编号 | 需求 |
|------|------|
| FR-A9.1 | 输出目录支持本地 SAF 目录与 WebDAV 网络位置两类目标 |
| FR-A9.2 | WebDAV 走协议客户端（WebDAVClient），与桌面版共享 `WebDavFileStorage` 实现 |
| FR-A9.3 | 连接配置提供"测试连接"即时验证（认证 + 写权限探测） |
| FR-A9.4 | WebDAV 目标下仅提供 copy，move 选项禁用并给出原因提示 |
| FR-A9.5 | 分块流式拷贝（8MB 缓冲），逐文件 + 总体双进度；网络中断自动重试 3 次（指数退避） |
| FR-A9.6 | **支持 SMB**（ADR-0007）：SMBLibrary 纯托管客户端，双端统一实现；SMB profile 的用户名/密码显式 NTLM 认证（首版不支持 guest）；仅作输出目标，仅 copy |
| FR-A9.7 | SMB 传输与 WebDAV 同一套可靠性语义：8MB 分块流式、`.mo-tmp` 临时名 + 大小校验、中断重试 3 次（指数退避）、WakeLock 保持 |

### FR-A10 凭据管理（P0）

| 编号 | 需求 |
|------|------|
| FR-A10.1 | 凭据随连接配置保存在 config.json（应用专有目录） |
| FR-A10.2 | Android 使用 Keystore 生成 AES 密钥加密凭据（实现 `ICredentialCrypto` 接口） |
| FR-A10.3 | 密钥不可导出（Keystore 硬件-backed，设备绑定） |
| FR-A10.4 | 日志与报告中严禁出现密码明文 |

---

## 4. 界面需求

以 `docs/android-prototype/index.html` 为准（Avalonia 12 Android，Material 风格）。要点：

- **导航**：顶部 AppBar（汉堡菜单 + 标题）+ 侧滑抽屉（整理工作台 / 失败文件 / 分析报告 / 设置）
- **整理工作台**：目录选取（SAF）→ 操作参数 → 分析按钮 → 统计摘要 → 执行按钮，纵向滚动
- **失败文件**：列表视图（文件名 + 原因 chip），点击展开详情；底部批量操作栏
- **分析报告**：纯文本滚动视图 + 另存为/分享
- **设置**：分组卡片（提取器 / 文件名模式 / 网络位置 / 扫描参数），纵向滚动
- **状态栏**：底部固定，显示当前操作进度与提示
- 所有交互目标 ≥ 48dp 触摸热区

---

## 5. 数据与接口

### 5.1 config.json

结构与桌面版一致（见桌面 SRS §5.1），存储路径为应用专有目录。差异：
- `networkProfiles` 支持 WebDAV 与 SMB 两类（2026-08-15 修订，ADR-0007；原"仅 WebDAV"作废）
- `paths.sourceDir` / `outputDir` / `pendingDir` 存储真实路径字符串；旧版或不可映射提供方为 `content://...` 树 URI（读取时按前缀分流）
- 密码加密前缀为 `KS:`（Keystore），非 `DPAPI:` / `B64:`

### 5.2 analysis-result.json

结构与桌面版一致（见桌面 SRS §5.2）。`path` 字段存储源标识符（真实路径或 SAF content URI，ADR-0006）。文件落盘位置为应用专有目录（不写 SAF 源目录）；`MediaFile.Source` 不参与序列化，读取结果后由 Scanner/Store 按 path 重新解析源。

### 5.3 patterns.json

结构与桌面版完全一致（见桌面 SRS §5.3），可在桌面版编辑后复制到 Android 设备。

### 5.4 TXT 分析报告

与桌面版格式一致（AnalysisReportGenerator 共享）。

---

## 6. 非功能需求

| 编号 | 类别 | 需求 |
|------|------|------|
| NFR-A1 | 性能 | 千张规模：分析吞吐 ≥ 50 文件/秒（手机存储，含 EXIF 读取）；UI 全程无阻塞 |
| NFR-A2 | 性能 | 内存峰值 ≤ 300MB（不缓存原图/缩略图） |
| NFR-A3 | 性能 | APK 包体 ≤ 40MB（.NET 运行时 + Core + Avalonia） |
| NFR-A4 | 可靠性 | 单个文件解析失败不得中断整体分析；所有 IO 操作有异常捕获 |
| NFR-A5 | 可靠性 | 执行阶段遇错（占用、权限）记录并继续，结束汇总报告 |
| NFR-A6 | 可移植 | Core 多目标编译，`#if ANDROID` 切换平台实现；桌面版不受影响 |
| NFR-A7 | 可测试 | Core 关键逻辑与桌面版共享测试；Android 专属逻辑（SAF/Keystore/ExifInterface）在 Android 项目内测试 |
| NFR-A8 | 安全 | 凭据使用 Android Keystore 加密；密钥不可导出；日志/报告不含凭据 |
| NFR-A9 | 电池 | 分析/执行过程持有 WakeLock 防止息屏中断；完成后释放 |
| NFR-A10 | 分发 | GitHub Release 发布 APK + 校验和；不上架 Google Play |

---

## 7. 验收标准（首发 v1.0-android）

1. 对含 ≥ 500 个混合格式文件的 SAF 目录完成分析，成功率与桌面版同目录结果偏差 ≤ 5%（ExifInterface vs Magick.NET 覆盖面差异）
2. copy 模式执行后，输出 SAF 目录结构与分析日期一致；同名 rename 策略正确追加序号
3. 分析/执行过程可取消且无残留部分写坏的状态文件
4. WebDAV 连接配置 → 测试连接通过 → 归档到 WebDAV 目标，结果与本地一致
5. WebDAV 目标下 move 选项禁用；密码在 config.json 中为 Keystore 密文
6. 抽屉导航流畅切换页面；返回键行为正确（关抽屉/退出）
7. 应用重启后 SAF 授权保持有效，无需重新选取目录

---

## 8. 风险

| 编号 | 风险 | 缓解 |
|------|------|------|
| R-A1 | ExifInterface 不支持 TIFF/BMP，EXIF 覆盖面低于桌面版 Magick.NET | 落入文件名提取器兜底；后续可引入第三方库 |
| R-A2 | SAF 批量文件操作性能（ContentResolver 查询开销） | AndroidFileScanner 批量查询 + 缓存 URI 映射 |
| R-A3 | Avalonia Android 性能/包体未验证 | MVP 先跑通核心流程；包体超 40MB 则评估 NativeAOT |
| R-A4 | ViewModel 跨平台耦合（桌面版 VM 可能有 Avalonia.Desktop 依赖） | 审查现有 VM，平台相关逻辑抽到接口 |
| R-A5 | Android Keystore 在不同厂商设备上行为差异 | 主流设备测试；降级方案：EncryptedSharedPreferences |
| R-A5b | SMBLibrary 不支持 SMB 3.1.1 加密/签名，强制加密的服务器连不上；手机 Wi-Fi 下长传稳定性 | 覆盖主流家用 NAS（群晖/威联通默认不强制加密）；失败给明确文案；WakeLock + 重试 + `.mo-tmp` 保护（ADR-0007） |
| R-A6 | 大文件 WebDAV 上传时网络中断 | 重试 3 次 + 指数退避（与桌面版一致）；WakeLock 保持上传 |
| R-A7 | 无 Play Store 分发，用户需手动更新 | GitHub Release + 应用内版本检查 |
