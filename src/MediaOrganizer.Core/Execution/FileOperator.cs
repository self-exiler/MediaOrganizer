using System.Collections.Concurrent;
using System.Diagnostics;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Planning;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Execution;

public sealed record FileOperationResult(
    int Succeeded, int Skipped, int Overwritten, int Renamed, int Failed,
    IReadOnlyList<string> Errors,
    TransferTimingReport? Timing = null);

/// <summary>
/// 执行耗时聚合（评估文档 M0 埋点）：metadata=碰撞检查/建目录/改名/时间戳等固定往返；
/// transfer=数据流拷贝（含重试的全部尝试）。TotalBytes 为全部尝试的写入字节数合计。
/// </summary>
public sealed record TransferTimingReport(
    int FileCount,
    double MetadataMeanMs, double MetadataP95Ms,
    double TransferMeanMs, double TransferP95Ms,
    long TotalBytes);

/// <summary>
/// 执行 copy，处理同名策略与 mtime 矫正（SRS FR-5.2/5.3/5.5，ADR-0004）。
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
    // 执行结束后统一 Dispose，避免每文件一个信号量永不释放的累积泄漏（评审 2.10）
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _targetGates = new();

    // M0 埋点：按毫秒收集各阶段耗时（含重试的每次尝试），执行结束聚合为 TransferTimingReport
    private readonly ConcurrentBag<double> _metaMs = new();
    private readonly ConcurrentBag<double> _transferMs = new();
    private long _totalBytes;

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

        // perf-8：仅计划内重名的目标需要互斥闸（并行下同名目标的 Exists 检查与传输交错会漏判）；
        // 重名几乎不出现，避免每文件一次字典 + 信号量分配。
        var duplicatedTargets = files.GroupBy(f => f.RelativeTarget)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

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
                    var sw = Stopwatch.StartNew();
                    var resolution = await ResolveCollisionAsync(f.RelativeTarget, token);
                    _metaMs.Add(sw.Elapsed.TotalMilliseconds);
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
                    gate?.Release();
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

        var timing = files.Count > 0 ? BuildTiming(files.Count) : null;
        return new FileOperationResult((int)ok, (int)skip, (int)overwrite, (int)rename, (int)fail, errors.ToArray(), timing);
    }

    /// <summary>聚合 M0 埋点：各阶段均值与 P95（按尝试次数计）。</summary>
    private TransferTimingReport BuildTiming(int fileCount)
        => new(
            fileCount,
            Mean(_metaMs), Percentile95(_metaMs),
            Mean(_transferMs), Percentile95(_transferMs),
            Interlocked.Read(ref _totalBytes));

    private static double Mean(ConcurrentBag<double> values)
        => values.IsEmpty ? 0 : values.Average();

    private static double Percentile95(ConcurrentBag<double> values)
    {
        if (values.IsEmpty) return 0;
        var sorted = values.ToArray();
        Array.Sort(sorted);
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1)];
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
                await TimeAsync(
                    () => _target.CreateDirectoryAsync(Path.GetDirectoryName(finalTarget)?.Replace('\\', '/') ?? "", ct),
                    _metaMs);
                try
                {
                    // P2-5：结果从 JSON 回读时 MediaFile.Source 未持久化而为 null（AnalysisResultStore 不落盘），
                    // 强解引用会得到 NullReferenceException 且信息无指导性 → 给出明确报错计入失败清单。
                    var source = f.Source.Source;
                    if (source is null)
                        throw new IOException("源对象缺少可读流（该结果源端不可用），无法执行");

                    // 大小校验（FR-10.7 P0）：写入自证——CopyFromAsync 返回实际写入字节数并与源端长度比对
                    // （SAF 下 FileInfo(path) 不可用），替代拷贝后重查目标长度的逐文件 3 次网络往返。
                    var expected = source.Length;
                    var actual = await TimeAsync(
                        () => _target.CopyFromAsync(source, temp, null, ct), _transferMs);
                    Interlocked.Add(ref _totalBytes, actual);
                    if (actual != expected)
                        throw new IOException($"大小校验失败：期望 {expected}，实际 {actual}");

                    await TimeAsync(() => _target.MoveAsync(temp, finalTarget, ct), _metaMs);
                }
                catch
                {
                    // 传输或改名失败：删掉目标端残留的 .mo-tmp，避免留下垃圾文件
                    await TryDeleteTempAsync(temp, ct);
                    throw;
                }

                if (_fixMtime)
                    await TimeAsync(() => _target.SetModifiedUtcAsync(finalTarget, f.Date.UtcDateTime, ct), _metaMs);
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

    /// <summary>计时包装：无论成败都记录该阶段耗时（含失败尝试，便于暴露真实瓶颈）。</summary>
    private static async Task<T> TimeAsync<T>(Func<Task<T>> operation, ConcurrentBag<double> sinkMs)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await operation();
        }
        finally
        {
            sinkMs.Add(sw.Elapsed.TotalMilliseconds);
        }
    }

    private static Task TimeAsync(Func<Task> operation, ConcurrentBag<double> sinkMs)
        => TimeAsync<object?>(async () =>
        {
            await operation();
            return null;
        }, sinkMs);

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
