# ADR-0007: Android SMB 支持（激活 SMBLibrary，双端统一）

- 状态：已接受（Accepted）
- 日期：2026-08-15
- 关联：ADR-0004（网络输出目标）、ADR-0005（Android 分支，§9 修订）、ADR-0006（源端抽象，决策 8 修订）
- 访谈方式：grill-with-docs 持续追问式访谈（2026-08-15）

## 背景

ADR-0005 §9 决策"Android 不支持 SMB"，其依据是"SMB = UNC 路径 = Windows 专属"。移植前评估发现该依据不成立：

1. 当前 `SmbFileStorage` 实为 `LocalFileStorage` 套 UNC 路径（`\\server\share`），走操作系统文件 API——"仅 Windows"的限制来自 UNC 方式，而非 SMB 协议本身。
2. `SMBLibrary 1.5.7.1`（纯托管 SMB1/2/3 客户端）**已在 Core 依赖中但全项目无一处使用**（死引用）。纯托管代码可在 net10.0-android 运行。
3. 既有缺陷：UNC 方式下 `NetworkProfile` 的用户名/密码被完全忽略（`new SmbFileStorage(profile.Address)` 未使用凭据，认证依赖 OS 会话）。

用户要求 Android 版支持 SMB。本 ADR 记录决策，并修订 ADR-0005 §9、ADR-0006 决策 8。

## 决策

### 1. 实现：重写 SmbFileStorage 为 SMBLibrary 客户端

- 重写 `SmbFileStorage` 为基于 SMBLibrary 的 `IFileStorage` 完整实现（保留类名，`StorageFactory` 语义不变）：
  - 连接参数自 `profile.Address` 解析（`\\server\share[\path]` 或 `server/share`），凭据取 `profile.Username` / `CredentialCrypto.Decrypt(profile.Password)`；
  - NTLM 显式登录；首版**不支持 guest/匿名**；
  - 实现全部接口方法：Exists / CreateDirectory（逐级）/ GetLength / CopyFromAsync / Delete / Move（同 share 内 rename）/ SetModifiedUtc；
  - `CopyFromAsync` 源参数按 ADR-0006 决策 3 为 IMediaSource：SAF→SMB 流式上传天然打通（ContentResolver 流 → SMB 写入流），`mo-tmp` 临时名、大小校验、3 次指数退避重试（FileOperator 层已有，无需重实现）。
    - **2026-08-30 勘误**：原文"沿用 8MB 分块"错误。SMBLibrary 的 `WriteFile` **不**按协商的 `MaxWriteSize` 自动分片，单次写入超过 `min(服务器 MaxWriteSize, SMB2Client.ClientMaxWriteSize = 1MB)` 会被服务器直接拒绝。已改为按协商上限分块，见 SRS FR-10.6。
- 连接管理：每个 FileOperator 执行周期内复用单个 SMB 连接（连接 + 登录开销大）；并行度沿用网络目标默认 4， SMB 写入线程安全由实现内串行化保证。

### 2. 双端统一切换

- 桌面版同步从 UNC 方式切换到 SMBLibrary 实现，**删除 UNC 旧实现**：
  - 收益：SMB 连接配置的用户名/密码真正生效（修复既有缺陷）；一套实现双端复用；macOS/Linux 桌面未来可直接用 SMB 而无需用户自行挂载（ADR-0004 的历史限制解除）；
  - 代价：桌面 SMB 行为变化（不再依赖 OS 凭据管理器/已挂载会话），需回归测试；README 相关说明更新。

### 3. 范围：仅输出目标

- SMB 在 Android 上仅作归档**输出目标**（与 ADR-0004"仅输出目录支持网络"一致）；Android 源目录仍为 SAF。
- "SMB 作分析源"（直接整理 NAS 上的文件）需源端实现 SMB 版 IMediaSource/FileScanner，工作量翻倍，明确**延后到后续迭代**。

### 4. 依赖与多目标（修订 ADR-0006 决策 8）

- `SMBLibrary` 包引用**全平台保留**（不再加 Android 条件排除——ADR-0006 决策 8 中该项作废）；`System.Security.Cryptography.ProtectedData` 仍按 TFM 排除。
- APK 体积影响：SMBLibrary 为纯托管小库（< 1MB），NFR-A3（≤ 40MB）不受影响。
  - **2026-08-30 勘误**：该预估不成立。实测 APK 为 **50.0MB**（主因为自包含 .NET 运行时 + Magick.NET + Avalonia，非 SMBLibrary 单项）。NFR-A3 已按 ADR-0008 决策 5 放宽至 ≤60MB。

### 5. 配置与 UI

- `networkProfiles[].type` 在 Android 端解禁 `Smb` 值（SRS-Android §5.1 修订）；设置页网络位置编辑器双端统一为 SMB/WebDAV 两类。
- WebDAV 的既有规则不变：网络目标（含 SMB）仅 copy；`TestConnectionAsync` 的探针流程对 SMB 同样适用。

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| SMBLibrary 不支持 SMB 3.1.1 加密/签名，强制加密的服务器（部分企业 NAS、Windows 默认开启加密的共享）连不上 | 主流家用 NAS（群晖/威联通默认 SMB2/3 不强制加密）覆盖良好；连接失败时返回明确错误文案；后续可评估 SMBLibrary 分支版本 |
| 手机 Wi-Fi 下 SMB 长传稳定性（息屏/切网） | WakeLock（NFR-A9）+ 3 次指数退避重试 + `.mo-tmp` 半成品保护 |
| 桌面版切换后行为差异（方言协商、大文件性能 vs OS 重定向器） | 里程碑 0 回归时加入 SMB 目标实测（≥ 1GB 混合文件）；保留 WebDAV 作为备选路径 |
| SMBLibrary 久未高频更新，潜在 bug 需自行兜底 | 接口层 IFileStorage 隔离，最差情况可替换实现；问题清单记入风险跟踪 |

## 对既有文档的修订

| 文档 | 修订点 |
|------|--------|
| ADR-0005 §9 | SMB 行：Android ❌ → ✅（SMBLibrary，本 ADR） |
| ADR-0006 决策 8 | "SMBLibrary 加条件排除"作废，全平台保留 |
| SRS-Android §1.2 / FR-A9.6 / §5.1 | SMB 从"不包含"移除；支持 SMB 输出目标；type 字段解禁 |
| glossary.md | ~~SMB（Android）~~ 条目转正；"网络位置"条目更新 |
