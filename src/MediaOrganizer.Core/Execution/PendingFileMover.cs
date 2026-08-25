using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Execution;

public sealed record PendingMoveResult(int Moved, int Failed, IReadOnlyList<string> MovedNames, IReadOnlyList<string> Errors);

/// <summary>
/// 失败文件批量移动到待处理目录（FR-A6.5，ADR-0006 决策 5）：
/// 复用 IMediaSource（源）+ IFileStorage（目标）——SAF→SAF / SAF→本地的移动语义 = 流式复制 + 删除源。
/// 同名冲突沿用 rename `_n` 规则（与 FileOperator.FindFreeNameAsync 语义一致）。
/// </summary>
public sealed class PendingFileMover
{
    /// <summary>逐个移动；单文件失败记录并继续（NFR-A4）。源端无 IMediaSource（结果回读场景）的条目跳过并计失败。</summary>
    public async Task<PendingMoveResult> MoveAsync(
        IReadOnlyList<MediaFile> files,
        IFileStorage target,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        int moved = 0, failed = 0;
        var errors = new List<string>();
        var movedNames = new List<string>();
        await target.CreateDirectoryAsync("", ct);

        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            if (file.Source is null)
            {
                failed++;
                errors.Add($"{file.FileName}: 源端不可用（结果回读后未重新解析）");
                continue;
            }

            try
            {
                var name = file.FileName;
                var relative = await NameCollisionResolver.FindFreeNameOnlyAsync(target, name, ct);
                await target.CopyFromAsync(file.Source, relative, null, ct);

                var expected = file.Source.Length;
                var actual = await target.GetLengthAsync(relative, ct);
                if (actual >= 0 && actual != expected)
                    throw new IOException($"大小校验失败：期望 {expected}，实际 {actual}");

                file.Source.Delete();
                moved++;
                movedNames.Add(file.FileName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{file.FileName}: {ex.Message}");
            }
            progress?.Report((double)(i + 1) / files.Count);
        }
        return new PendingMoveResult(moved, failed, movedNames, errors);
    }
}
