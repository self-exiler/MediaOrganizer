# ADR-0004: 网络输出目标（SMB / WebDAV）

- 状态：已接受（Accepted）
- 日期：2026-08-09
- 关联：ADR-0001（迁移）、ADR-0003（架构）、SRS FR-10/FR-11

## 背景

用户要求输出目录支持网络位置（NAS/网盘归档是常见场景）。访谈两轮后收敛为：SMB + WebDAV（明确弃 FTP）、应用内凭据、网络目标仅 copy、仅输出目录支持网络、分块流式传输、大小校验+重试、进 v1.0。

## 决策

### 1. 协议范围

| 协议 | 支持方式 | 限制 |
|------|---------|------|
| **SMB** | UNC 路径 `\\server\share\...`，走 OS 文件 API | v1.0 仅 Windows；macOS/Linux 需用户自行挂载后按本地路径使用（README 注明） |
| **WebDAV** | 协议客户端（NuGet `WebDAVClient`） | 全平台 |
| ~~FTP~~ | 不支持 | 明文协议老旧，NAS 主流场景已被 SMB/WebDAV 覆盖 |

### 2. 架构：文件存储抽象层

Core 新增 `IFileStorage` 抽象，执行层不再直接调 `File.*`：

```csharp
public interface IFileStorage
{
    bool Exists(string relativePath);
    void CreateDirectory(string relativePath);
    long GetLength(string relativePath);
    Task CopyToAsync(Stream source, string relativePath, IProgress<long>? progress, CancellationToken ct);
    void Delete(string relativePath);
    void SetModifiedUtc(string relativePath, DateTime utc);
}
```

实现：`LocalFileStorage`（默认）、`SmbFileStorage`（UNC 包装，复用 Local 语义）、`WebDavFileStorage`（HTTP）。归档规划（ArchivePlanner）不受影响，仅产出相对路径；`FileOperator` 改为面向 `IFileStorage`。

### 3. 凭据管理（FR-11）

- 网络位置以"连接配置"（profile）形式保存：名称、类型、主机/共享或 WebDAV URL、用户名、密码
- **Windows：DPAPI 加密存储密码**（`ProtectedData.Protect`，CurrentUser 域）
- **macOS/Linux：首版降级为 Base64 明文**，UI 显示警告，README 注明，后续版本接入 Keychain/Secret Service
- 提供"测试连接"按钮即时验证

### 4. 传输语义

- **网络目标仅提供 copy**，UI 上 move 选项在网络目标下禁用（防止误删源文件）
- 分块流式拷贝（8MB 缓冲），逐文件+总体双进度上报
- 完成校验：目标文件大小 == 源大小；失败自动重试 3 次（指数退避），仍失败计入错误清单
- 半成品处理：拷贝前写 `<name>.mo-tmp`，成功后改名
- mtime 矫正：SMB 走 SetLastWriteTime；WebDAV 走 PROPPATCH getlastmodified（服务端不支持则静默跳过）

### 5. 版本规划

全部进 v1.0。`IFileStorage` 抽象先行，本地执行路径立即重构（零行为变化，测试保证），网络实现在其上加。

## 后果

- 正面：NAS/网盘归档开箱即用；抽象层为将来加 FTP/S3/云盘留口
- 负面：FileOperator 重构（单测需重写校验部分）；SMB 跨平台限制需要文档澄清
- 风险：WebDAV 服务端实现差异（PROPPATCH、分块、超时）→ 用真实 NAS/坚果云做兼容性 Spike 后定稿
