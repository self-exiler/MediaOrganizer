using System.Collections.Concurrent;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Platforms;
using MediaOrganizer.Core.Planning;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Execution;

public sealed record FileOperationResult(
    int Succeeded, int Skipped, int Overwritten, int Renamed, int Failed,
    IReadOnlyList<string> Errors,
    // 成功落盘的源标识符（MediaFile.Path）：待处理移动等需要按文件回执的调用方使用
    IReadOnlyList<string>? SucceededPaths = null);

/// <summary>
/// 执行 copy，处理同名策略与 mtime 矫正（SRS FR-5.2/5.3/5.5）。
/// 面向 IFileStorage 抽象：本地/SMB/WebDAV 统一语义。
/// 网络传输约定：临时名 .mo-tmp → 写入自证大小校验 → 改名；失败重试 ≤3 次（指数退避 1/2s，与前两次尝试次数对齐）。
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
    // 执行结束后统一 Dispose，避免每文件一个信号量永不释放的累积泄漏
    // 大小写比较器：Windows/SMB 文件系统不区分大小写，IMG.JPG 与 img.jpg 必须共用同一把闸
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _targetGates = new(StringComparer.OrdinalIgnoreCase);

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
        var succeededPaths = new ConcurrentBag<string>();
        var files = plan.Files;
        long processed = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxDegreeOfParallelism,
            CancellationToken = ct
        };

        // 仅计划内重名的目标需要互斥闸（并行下同名目标的 Exists 检查与传输交错会漏判）；
        // 重名几乎不出现，避免每文件一次字典 + 信号量分配。
        var duplicatedTargets = files.GroupBy(f => f.RelativeTarget, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            await Parallel.ForEachAsync(files, options, async (f, token) =>
            {
                token.ThrowIfCancellationRequested();

                var gate = duplicatedTargets.Contains(f.RelativeTarget)
                    ? _targetGates.GetOrAdd(f.RelativeTarget, _ => new SemaphoreSlim(1, 1))
                    : null;
                if (gate is not null) await gate.WaitAsync(token);
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
                        await TransferWithRetryAsync(f, temp, resolution.Target,
                            resolution.Action == CollisionAction.Overwrite, token);

                        // move 语义：目标确认落盘后删除源（网络目标下 GUI 已禁用 move，此处防御性生效）
                        // SAF→SAF / SAF→网络的移动 = 流式复制 + 删除源，由 IMediaSource.Delete 保证
                        if (_operation == FileOperation.Move)
                            f.Source.Source?.Delete();

                        succeededPaths.Add(f.Source.Path);
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
                    gate?.Release();
                }
                var done = Interlocked.Increment(ref processed);
                progress?.Report((double)done / files.Count);
            });
        }
        finally
        {
            // 执行结束（含取消）后统一释放按路径的信号量，避免永久驻留累积；
            // ForEachAsync 抛出前所有迭代体已完成，此处 Dispose 无并发等待者。
            foreach (var gate in _targetGates.Values) gate.Dispose();
            _targetGates.Clear();
        }

        return new FileOperationResult((int)ok, (int)skip, (int)overwrite, (int)rename, (int)fail,
            errors.ToArray(), succeededPaths.ToArray());
    }

    private enum CollisionAction { None, Skip, Overwrite, Rename }

    private sealed record CollisionResolution(string? Target, CollisionAction Action);

    private async Task<CollisionResolution> ResolveCollisionAsync(string relativeTarget, CancellationToken ct)
    {
        if (!await ExistsWithRetryAsync(relativeTarget, ct))
            return new CollisionResolution(relativeTarget, CollisionAction.None);

        switch (_existAction)
        {
            case ExistAction.Skip:
                return new CollisionResolution(null, CollisionAction.Skip);
            case ExistAction.Overwrite:
                // 删除时机挪到传输层：临时文件写完、即将改名前才删旧文件。
                // 若在碰撞解析阶段先删，网络中断且重试耗尽时新旧俱失。
                return new CollisionResolution(relativeTarget, CollisionAction.Overwrite);
            default:
                var free = await FindFreeNameAsync(relativeTarget, ct);
                return new CollisionResolution(free, CollisionAction.Rename);
        }
    }

    private async Task TransferWithRetryAsync(PlannedFile f, string temp, string finalTarget, bool deleteBeforeMove, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _target.CreateDirectoryAsync(Path.GetDirectoryName(finalTarget)?.Replace('\\', '/') ?? "", ct);
                try
                {
                    // 结果回读场景 MediaFile.Source 可能为 null，强解引用会得到 NullReferenceException 且信息无指导性 → 给出明确报错计入失败清单。
                    var source = f.Source.Source;
                    if (source is null)
                        throw new IOException("源对象缺少可读流（该结果源端不可用），无法执行");

                    // 大小校验（FR-10.7 P0）：写入自证——CopyFromAsync 返回实际写入字节数并与源端长度比对
                    // （SAF 下 FileInfo(path) 不可用），替代拷贝后重查目标长度的逐文件 3 次网络往返。
                    var expected = source.Length;
                    var actual = await _target.CopyFromAsync(source, temp, null, ct);
                    if (actual != expected)
                        throw new IOException($"大小校验失败：期望 {expected}，实际 {actual}");

                    // Overwrite：新数据已完整落盘到 .mo-tmp，此刻才删旧文件
                    if (deleteBeforeMove && await _target.ExistsAsync(finalTarget, ct))
                        await _target.DeleteAsync(finalTarget, ct);

                    await _target.MoveAsync(temp, finalTarget, ct);
                }
                catch
                {
                    // 传输或改名失败：删掉目标端残留的 .mo-tmp，避免留下垃圾文件。
                    // 用 None 而非 ct：取消场景下 ct 已失效，SMB 闸的 WaitAsync(ct) 会立即抛出导致清理被吞
                    await TryDeleteTempAsync(temp, CancellationToken.None);
                    throw;
                }

                // 数据已落盘，mtime 矫正失败只降级为警告，绝不触发整文件重传
                if (_fixMtime)
                {
                    try
                    {
                        await _target.SetModifiedUtcAsync(finalTarget, f.Date.UtcDateTime, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        PlatformServices.Current.Log($"[FileOperator] 时间戳设置失败（文件已归档成功）{finalTarget}：{ex.Message}");
                    }
                }
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

    /// <summary>存在检查与传输同等待遇：瞬时故障（网络存储的 Exists 上抛）走同样的 ≤3 次退避重试。</summary>
    private async Task<bool> ExistsWithRetryAsync(string relativeTarget, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _target.ExistsAsync(relativeTarget, ct);
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

    private Task<string> FindFreeNameAsync(string relativeTarget, CancellationToken ct)
        => NameCollisionResolver.FindFreeAsync(_target, relativeTarget, ct);
}
