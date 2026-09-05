using System.Collections.Concurrent;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Planning;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Execution;

public sealed record FileOperationResult(
    int Succeeded, int Skipped, int Overwritten, int Renamed, int Failed,
    IReadOnlyList<string> Errors);

/// <summary>
/// 执行 copy，处理同名策略与 mtime 矫正（SRS FR-5.2/5.3/5.5，ADR-0004）。
/// 面向 IFileStorage 抽象：本地/SMB/WebDAV 统一语义。
/// 网络传输约定：临时名 .mo-tmp → 大小校验 → 改名；失败重试 ≤3 次（指数退避 1/2s，与前两次尝试次数对齐）。
/// 支持并行执行（本地默认 2 / 网络默认 4），通过 maxDegreeOfParallelism 控制。
/// </summary>
public sealed class FileOperator
{
    public const int MaxRetries = 3;
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    private readonly IFileStorage _target;
    private readonly FileOperation _operation;
    private readonly ExistAction _existAction;
    private readonly bool _fixMtime;
    private readonly int _maxDegreeOfParallelism;
    // 同一原始目标路径的碰撞检查 + 传输必须串行：并行下 Exists 检查与临时文件落盘交错会漏判同名（覆盖丢文件/漏计 Skipped）。
    // 执行结束后统一 Dispose，避免每文件一个信号量永不释放的累积泄漏（评审 2.10）
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _targetGates = new();

    public FileOperator(IFileStorage target, FileOperation operation, ExistAction existAction, bool fixMtime, int maxDegreeOfParallelism = 2)
    {
        _target = target;
        _operation = operation;
        _existAction = existAction;
        _fixMtime = fixMtime;
        _maxDegreeOfParallelism = maxDegreeOfParallelism <= 0 ? 2 : maxDegreeOfParallelism;
    }

    public async Task<FileOperationResult> ExecuteAsync(ArchivePlan plan, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        long ok = 0, skip = 0, overwrite = 0, rename = 0, fail = 0;
        var errors = new ConcurrentBag<string>();
        var files = plan.Files;
        long processed = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxDegreeOfParallelism,
            CancellationToken = ct
        };

        try
        {
            await Parallel.ForEachAsync(files, options, async (f, token) =>
            {
                token.ThrowIfCancellationRequested();

                var gate = _targetGates.GetOrAdd(f.RelativeTarget, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(token);
                try
                {
                    var resolution = await ResolveCollisionAsync(f.RelativeTarget, token);
                    if (resolution.Target is null)
                    {
                        switch (resolution.Action)
                        {
                            case CollisionAction.Skip: Interlocked.Increment(ref skip); break;
                            case CollisionAction.Overwrite: Interlocked.Increment(ref overwrite); break;
                            case CollisionAction.Rename: Interlocked.Increment(ref rename); break;
                        }
                    }
                    else
                    {
                        if (resolution.Action == CollisionAction.Overwrite) Interlocked.Increment(ref overwrite);
                        if (resolution.Action == CollisionAction.Rename) Interlocked.Increment(ref rename);

                        var temp = f.RelativeTarget + ".mo-tmp";
                        await TransferWithRetryAsync(f, temp, resolution.Target, token);

                        // move 语义：目标确认落盘后删除源（网络目标下 GUI 已禁用 move，此处防御性生效）
                        // SAF→SAF / SAF→网络的移动 = 流式复制 + 删除源，由 IMediaSource.Delete 保证（ADR-0006 决策 3）
                        if (_operation == FileOperation.Move)
                            f.Source.Source?.Delete();

                        Interlocked.Increment(ref ok);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref fail);
                    errors.Add($"{f.Source.FileName} -> {f.RelativeTarget}: {ex.Message}");
                }
                finally
                {
                    gate.Release();
                }
                var done = Interlocked.Increment(ref processed);
                progress?.Report((double)done / files.Count);
            });
        }
        finally
        {
            // 执行结束（含取消）后统一释放按路径的信号量，避免永久驻留累积（评审 2.10）；
            // ForEachAsync 抛出前所有迭代体已完成，此处 Dispose 无并发等待者。
            foreach (var gate in _targetGates.Values) gate.Dispose();
            _targetGates.Clear();
        }

        return new FileOperationResult((int)ok, (int)skip, (int)overwrite, (int)rename, (int)fail, errors.ToArray());
    }

    private enum CollisionAction { None, Skip, Overwrite, Rename }

    private sealed record CollisionResolution(string? Target, CollisionAction Action);

    private async Task<CollisionResolution> ResolveCollisionAsync(string relativeTarget, CancellationToken ct)
    {
        if (!await _target.ExistsAsync(relativeTarget, ct))
            return new CollisionResolution(relativeTarget, CollisionAction.None);

        switch (_existAction)
        {
            case ExistAction.Skip:
                return new CollisionResolution(null, CollisionAction.Skip);
            case ExistAction.Overwrite:
                await _target.DeleteAsync(relativeTarget, ct);
                return new CollisionResolution(relativeTarget, CollisionAction.Overwrite);
            default:
                var free = await FindFreeNameAsync(relativeTarget, ct);
                return new CollisionResolution(free, CollisionAction.Rename);
        }
    }

    private async Task TransferWithRetryAsync(PlannedFile f, string temp, string finalTarget, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _target.CreateDirectoryAsync(Path.GetDirectoryName(finalTarget)?.Replace('\\', '/') ?? "", ct);
                try
                {
                    // P2-5：结果从 JSON 回读时 MediaFile.Source 未持久化而为 null（AnalysisResultStore 不落盘），
                    // 强解引用会得到 NullReferenceException 且信息无指导性 → 给出明确报错计入失败清单。
                    if (f.Source.Source is null)
                        throw new IOException("源对象缺少可读流（该结果源端不可用），无法执行");
                    await _target.CopyFromAsync(f.Source.Source, temp, null, ct);

                    // 大小校验（FR-10.7 P0）：长度取源端抽象（SAF 下 FileInfo(path) 不可用）。
                    // 目标取不到长度（-1）时重查一次；仍取不到则判失败，不得静默放行（评审 2.8：截断文件会被记为成功）
                    var expected = f.Source.Source?.Length ?? f.Source.Size;
                    var actual = await _target.GetLengthAsync(temp, ct);
                    if (actual < 0)
                        actual = await _target.GetLengthAsync(temp, ct);
                    if (actual < 0)
                        throw new IOException($"无法获取目标文件长度，大小校验不可用：{temp}");
                    if (actual != expected)
                        throw new IOException($"大小校验失败：期望 {expected}，实际 {actual}");

                    await _target.MoveAsync(temp, finalTarget, ct);
                }
                catch
                {
                    // 传输或改名失败：删掉目标端残留的 .mo-tmp，避免留下垃圾文件
                    await TryDeleteTempAsync(temp, ct);
                    throw;
                }

                if (_fixMtime)
                    await _target.SetModifiedUtcAsync(finalTarget, f.Date.UtcDateTime, ct);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(Backoff[Math.Min(attempt, Backoff.Length - 1)], ct);
            }
        }
    }

    /// <summary>尽力删除失败残留的临时文件；删除本身失败不影响主错误。</summary>
    private async Task TryDeleteTempAsync(string temp, CancellationToken ct)
    {
        try
        {
            await _target.DeleteAsync(temp, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileOperator] 清理临时文件失败 {temp}：{ex.Message}");
        }
    }

    private Task<string> FindFreeNameAsync(string relativeTarget, CancellationToken ct)
        => NameCollisionResolver.FindFreeAsync(_target, relativeTarget, ct);
}
