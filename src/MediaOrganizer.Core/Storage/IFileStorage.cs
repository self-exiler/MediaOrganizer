using MediaOrganizer.Core.Sources;

namespace MediaOrganizer.Core.Storage;

/// <summary>
/// 目标文件存储抽象（ADR-0004 / ADR-0006 决策 3）：本地 / SMB(SMBLibrary) / WebDAV / Android SAF 实现。
/// 所有路径为归档计划产出的相对路径（'/' 分隔），由实现负责解析到目标根。
/// 源参数为 IMediaSource——SAF→本地 / SAF→网络的流式拷贝天然打通。
/// </summary>
public interface IFileStorage
{
    Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);

    /// <summary>逐级创建目录（幂等）。</summary>
    Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default);

    /// <summary>目标文件字节长度；不可用返回 -1。</summary>
    Task<long> GetLengthAsync(string relativePath, CancellationToken ct = default);

    /// <summary>从源端抽象分块流式拷贝到目标（8MB 缓冲），逐字节回报进度。</summary>
    Task CopyFromAsync(IMediaSource source, string relativeTarget, IProgress<long>? progress = null, CancellationToken ct = default);

    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>移动/重命名（临时名 → 最终名）。</summary>
    Task MoveAsync(string relativeFrom, string relativeTo, CancellationToken ct = default);

    /// <summary>设置目标文件修改时间（不支持则静默跳过）。</summary>
    Task SetModifiedUtcAsync(string relativePath, DateTime utc, CancellationToken ct = default);
}
