using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Planning;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Execution;

public sealed record PendingMoveResult(int Moved, int Failed, IReadOnlyList<string> MovedIdentifiers, IReadOnlyList<string> Errors);

/// <summary>
/// 失败文件批量移动到待处理目录（FR-A6.5）：
/// 复用 FileOperator（Move + Rename，目标平铺文件名）——临时名 .mo-tmp、写入自证大小校验、
/// 查重名退避、大小写互斥闸与删除时机全部与主流程同一套实现（精简项：曾各写一套且校验口径不同）。
/// </summary>
public sealed class PendingFileMover
{
    /// <summary>逐个移动；单文件失败记录并继续（NFR-A4）。源端无 IMediaSource 的条目由 FileOperator 计入失败清单。</summary>
    public async Task<PendingMoveResult> MoveAsync(
        IReadOnlyList<MediaFile> files,
        IFileStorage target,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        // 平铺目标名：清洗路径分隔符，防同名解析把 PendingFile 的 Path 当目录结构
        var planned = files
            .Select(f => new PlannedFile(f, DateTimeOffset.Now,
                f.FileName.Replace('\\', '_').Replace('/', '_')))
            .ToArray();
        var result = new AnalysisResult("", DateTimeOffset.Now, [], []);
        var plan = new ArchivePlan(result, "", ClassificationLevel.Day, planned);

        var r = await new FileOperator(target, FileOperation.Move, ExistAction.Rename, fixMtime: false)
            .ExecuteAsync(plan, progress, ct);

        return new PendingMoveResult(r.Succeeded, r.Failed, r.SucceededPaths ?? [], r.Errors);
    }
}
