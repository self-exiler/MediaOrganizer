using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Execution;

/// <summary>文件名查重：stem_n.ext 规则统一实现（由 FileOperator 唯一调用）。</summary>
public static class NameCollisionResolver
{
    /// <summary>单次探测的最大候选数：ExistsAsync 持续返回 true（故障/缓存异常）时不至于无限循环。</summary>
    private const int MaxCandidates = 10000;

    /// <summary>
    /// 返回第一个不存在的 stem_n.ext 相对路径，以及下次应从这个编号继续探测。
    /// startFrom 由调用方按目录复用，避免 N 个同名文件反复从 _1 探测造成的 O(N²) 网络往返（走查 #14）。
    /// </summary>
    public static async Task<(string Path, int NextStart)> FindFreeAsync(
        IFileStorage target, string relativePath, int startFrom, CancellationToken ct)
    {
        if (!await target.ExistsAsync(relativePath, ct)) return (relativePath, Math.Max(1, startFrom));

        var (dir, leaf) = PlanPath.Split(relativePath);
        var name = Path.GetFileNameWithoutExtension(leaf);
        var ext = Path.GetExtension(leaf);
        var first = Math.Max(1, startFrom);
        for (var i = first; i < first + MaxCandidates; i++)
        {
            var candidate = dir.Length == 0 ? $"{name}_{i}{ext}" : $"{dir}/{name}_{i}{ext}";
            if (!await target.ExistsAsync(candidate, ct)) return (candidate, i + 1);
        }
        throw new IOException($"重名探测超过 {MaxCandidates} 个候选仍无空位：{relativePath}");
    }
}
