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
/// 网络传输约定：临时名 .mo-tmp → 大小校验 → 改名；失败重试 ≤3 次（指数退避 1/2/4s）。
/// 支持并行执行（本地默认 2 / 网络默认 4），通过 maxDegreeOfParallelism 控制。
/// </summary>
public sealed class FileOperator
{
    public const int MaxRetries = 3;
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    private readonly IFileStorage _target;
    private readonly FileOperation _operation;
    private readonly ExistAction _existAction;
    private readonly bool _fixMtime;
    private readonly int _maxDegreeOfParallelism;
    // 同一原始目标路径的碰撞检查 + 传输必须串行：并行下 Exists 检查与临时文件落盘交错会漏判同名（覆盖丢文件/漏计 Skipped）
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
                await _target.CopyFromAsync(f.Source.Source!, temp, null, ct);

                // 大小校验（目标获取不到长度时跳过）；长度取源端抽象（SAF 下 FileInfo(path) 不可用）
                var expected = f.Source.Source?.Length ?? f.Source.Size;
                var actual = await _target.GetLengthAsync(temp, ct);
                if (actual >= 0 && actual != expected)
                    throw new IOException($"大小校验失败：期望 {expected}，实际 {actual}");

                await _target.MoveAsync(temp, finalTarget, ct);
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

    private async Task<string> FindFreeNameAsync(string relativeTarget, CancellationToken ct)
    {
        var dir = relativeTarget[..relativeTarget.LastIndexOf('/')];
        var name = Path.GetFileNameWithoutExtension(relativeTarget);
        var ext = Path.GetExtension(relativeTarget);
        for (var i = 1; ; i++)
        {
            var candidate = dir.Length == 0 ? $"{name}_{i}{ext}" : $"{dir}/{name}_{i}{ext}";
            if (!await _target.ExistsAsync(candidate, ct)) return candidate;
        }
    }

    /// <summary>本地文件系统查重名：返回追加 _n 且不存在的目标路径（失败文件批量移动等本地场景用）。</summary>
    public static string FindFreeLocalPath(string targetPath)
    {
        var dir = Path.GetDirectoryName(targetPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(targetPath);
        var ext = Path.GetExtension(targetPath);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
