# 领域词汇表（Glossary）

> C# 版统一领域语言（访谈后修订版，与 ADR-0002 范围一致）。标注 ~~删除线~~ 的为 Python 版存在但 C# 版已删除的概念，保留记录以便对照旧代码。

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
| 魔术工具 | Magic Tools | 可视化正则模式编辑器（C# 版完整保留）：字符着色、交互点选生成正则、智能变体、实时测试 |
| mtime 矫正 | mtime fix | 整理后可选将文件修改时间设为提取日期，便于资源管理器排序 |
| 网络位置 | network target | 输出目录的扩展：SMB（UNC）或 WebDAV 连接配置（ADR-0004，v1.0 新增） |
| 连接配置 | network profile | 一条网络位置记录：名称/协议/地址/用户名/密码（密码加密存储） |
| 文件存储抽象 | IFileStorage | 执行层文件操作抽象：Local / Smb / WebDav 三种实现 |
| 临时名传输 | .mo-tmp | 网络拷贝先写 `<name>.mo-tmp`，成功且大小校验一致后改名，杜绝半成品 |
| 大小校验 | size verification | 网络传输完成的判定：目标大小 == 源大小；失败重试 3 次（指数退避） |
| ~~FTP 支持~~ | — | 明确不支持（ADR-0004） |
| ~~加权投票~~ | — | 旧称，见"加权链" |
| ~~详细模式~~ | log_mode detailed | Python 版的 SQLite+MD5 指纹日志，C# 版已删除 |
| ~~link 操作~~ | hard/symbolic link | 已删除，只保留 copy/move |
| ~~元数据编辑~~ | EXIF write / ©day | 已删除（写 EXIF、PNG 文本块、视频标签均不做） |
| ~~合规文件名~~ | — | 已删除（`年-月-日-时-分-秒_随机字符` 重命名功能） |
| ~~魔术工具外置报告~~ | pattern report | 正则模式分析报告，已删除 |
