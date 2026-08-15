# 领域词汇表（Glossary）

> C# 版统一领域语言（访谈后修订版，与 ADR-0002 范围一致）。标注 ~~删除线~~ 的为 Python 版存在但 C# 版已删除的概念，保留记录以便对照旧代码。Android 分支术语见文末（ADR-0005）。

| 术语 | 英文/代码对应 | 定义 |
|------|--------------|------|
| 分析（Analyze） | analyze | 扫描源目录、对每个文件做日期提取、产出分析结果 JSON 与 TXT 报告的阶段 |
| 执行（Execute） | execute | 读取分析结果，按日期建目录并执行 copy/move 的阶段 |
| 提取器（Extractor） | IDateExtractor | 一种日期来源策略。C# 版收敛为 3 个：Exif / FileName / FileSystem |
| 加权链 | weighted extractor chain | 按权重降序依次尝试提取器，取第一个有效日期（Python 文档称"加权投票"，实为优先级链） |
| 文件名模式 | pattern | 一条带 group_mapping 的正则，用于从文件名解析日期；JSON 结构重新设计 |
| 结构指纹 | structure fingerprint | 文件名字符分类为 D(数字)/L(字母)/S(符号) 的指纹串，用于排序聚类与智能变体生成 |
| 日期来源 | date_source | 最终采用日期来自哪个提取器（Exif/FileName/FileSystem） |
| 失败文件 | unparsed files | 所有提取器均未得到有效日期的文件；唯一补救路径：魔术工具加规则 → 重新分析，或批量移动到待处理文件夹 |
| 待处理文件夹 | pending folder | 失败文件批量移动的目标位置 |
| 分级目录 | classification_level | year / month / day 三级目标目录结构 |
| 同名处理 | exist_action | skip / overwrite / rename |
| 未来日期文件 | FutureDate | 超出缓冲天数的未来日期文件，归入 FutureDate/ |
| 魔术工具 | Magic Tools | 可视化正则模式编辑器（桌面版完整保留）：字符着色、交互点选生成正则、智能变体、实时测试。**Android 版不含** |
| mtime 矫正 | mtime fix | 整理后可选将文件修改时间设为提取日期，便于资源管理器排序 |
| 网络位置 | network target | 输出目录的扩展：SMB 或 WebDAV 连接配置（ADR-0004/0005；ADR-0007 起 SMB 双端均走 SMBLibrary，仅输出目标） |
| 连接配置 | network profile | 一条网络位置记录：名称/协议/地址/用户名/密码（密码加密存储） |
| 文件存储抽象 | IFileStorage | 执行层文件操作抽象：Local / Smb / WebDav / AndroidSaf 四种实现（ADR-0006 起 CopyFromAsync 源参数为 IMediaSource） |
| 临时名传输 | .mo-tmp | 网络拷贝先写 `<name>.mo-tmp`，成功且大小校验一致后改名，杜绝半成品 |
| 大小校验 | size verification | 网络传输完成的判定：目标大小 == 源大小；失败重试 3 次（指数退避） |
| ~~FTP 支持~~ | — | 明确不支持（ADR-0004） |
| ~~加权投票~~ | — | 旧称，见"加权链" |
| ~~详细模式~~ | log_mode detailed | Python 版的 SQLite+MD5 指纹日志，C# 版已删除 |
| ~~link 操作~~ | hard/symbolic link | 已删除，只保留 copy/move |
| ~~元数据编辑~~ | EXIF write / ©day | 已删除（写 EXIF、PNG 文本块、视频标签均不做） |
| ~~合规文件名~~ | — | 已删除（`年-月-日-时-分-秒_随机字符` 重命名功能） |
| ~~魔术工具外置报告~~ | pattern report | 正则模式分析报告，已删除 |

## Android 分支术语（ADR-0005，2026-08-13）

| 术语 | 英文/代码对应 | 定义 |
|------|--------------|------|
| SAF | Storage Access Framework | Android 系统级文件选择框架；用户授权目录后返回 `content://` 树 URI，应用通过 ContentResolver 访问 |
| 树 URI | content tree URI | SAF 返回的 `content://` URI，代表用户选定的目录树；持久化授权后跨会话可用 |
| ContentResolver | ContentResolver | Android 系统 API，通过 URI 访问文件/目录（打开流、查询、创建/删除） |
| DocumentFile | DocumentFile | SAF 的高级封装类，提供类似 File 的树状遍历 API |
| Scoped Storage | Scoped Storage | Android 10+ 的存储隔离模型；应用只能直接访问专有目录，共享存储须走 SAF |
| AndroidSafStorage | AndroidSafStorage | IFileStorage 的 Android 实现，内部走 ContentResolver + DocumentFile |
| AndroidFileScanner | AndroidFileScanner | FileScanner 的 Android 对应物，遍历 DocumentFile.listFiles() |
| Android Keystore | AndroidKeyStore | Android 系统密钥库；生成 AES 密钥加密凭据，密钥不可导出 |
| ICredentialCrypto | ICredentialCrypto | Core 新增的凭据加密抽象；桌面用 DPAPI，Android 用 Keystore |
| IExifReader | IExifReader | Core 新增的图片 EXIF 读取抽象；桌面用 Magick.NET，Android 用 ExifInterface |
| ExifInterface | Android.Media.ExifInterface | Android 原生 EXIF 读取 API；支持 jpg/heic，不支持 tiff/bmp |
| 应用专有目录 | app-specific dir | `getExternalFilesDir(null)`；无需权限，卸载自动清除；Android 版配置文件存此处 |
| 多目标 | multi-target | Core.csproj 同时编译 net10.0 和 net10.0-android；同一份代码，`#if ANDROID` 切换平台实现 |
| 抽屉导航 | Drawer navigation | Android 版的导航模式（替代桌面版的左侧 Tab），侧滑抽屉切换页面 |
| SMB（Android） | SmbFileStorage (SMBLibrary) | 2026-08-15 转正（ADR-0007）：激活纯托管 SMBLibrary 双端统一实现，替代 UNC 方式；仅输出目标、仅 copy、NTLM 显式认证（首版不支持 guest）；~~SMB 作分析源~~ 延后迭代 |

## 移植前置重构术语（ADR-0006，2026-08-15）

| 术语 | 英文/代码对应 | 定义 |
|------|--------------|------|
| 源端抽象 | IMediaSource | Core 新增的"读源文件"抽象：Identifier / DisplayName / Length / ModifiedTime / OpenRead() / Delete()；本地与 SAF 双实现 |
| 源标识符 | source identifier | MediaFile.Path 的新语义：本地路径或 content:// URI；字段名不变，JSON 结构不变 |
| 显示名 | DisplayName | 源文件名（SAF 下取 DocumentFile.Name）；文件名模式提取与扩展名判断改走此属性，不再解析 URI 字符串 |
| TagLib 流抽象 | StreamFileAbstraction | TagLib.File.IFileAbstraction 的自定义实现，让 TagLib 从任意 Stream 读视频元数据；双端共享同一份解析代码 |
| 共享层 | MediaOrganizer.Shared | 新增 net10.0 项目：4 个共享 VM（工作台/失败文件/报告/设置）+ IFolderPicker / IFileSaver 平台抽象 |
| 目录选取抽象 | IFolderPicker | 共享 VM 选取目录的接口：桌面走 StorageProvider，Android 走 SAF Intent |
| 待处理移动服务 | PendingFileMover | Core 服务：批量移动失败文件到待处理目录；SAF→SAF 移动语义 = 流式复制 + 删除源 |
| 里程碑 0 | refactor milestone | ADR-0006 定义的 Android 开发前置重构：桌面版行为不变、现有测试全绿为完成标准 |
