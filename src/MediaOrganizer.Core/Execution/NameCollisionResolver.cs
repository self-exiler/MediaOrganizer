using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Execution;

/// <summary>文件名查重帮助：stem_n.ext 规则统一实现，供 FileOperator 和 PendingFileMover 共用。</summary>
public static class NameCollisionResolver
{
    /// <summary>异步查重名：用 IFileStorage.ExistsAsync 检查，返回第一个不存在的 stem_n.ext 相对路径。</summary>
    public static async Task<string> FindFreeAsync(
        IFileStorage target, string relativePath, CancellationToken ct)
    {
        if (!await target.ExistsAsync(relativePath, ct)) return relativePath;

        // 防御：路径不含 '/' 时退化为纯文件名查重，避免切片越界
        var sep = relativePath.LastIndexOf('/');
        var dir = sep <= 0 ? "" : relativePath[..sep];
        var name = Path.GetFileNameWithoutExtension(relativePath);
        var ext = Path.GetExtension(relativePath);
        for (var i = 1; ; i++)
        {
            var candidate = dir.Length == 0 ? $"{name}_{i}{ext}" : $"{dir}/{name}_{i}{ext}";
            if (!await target.ExistsAsync(candidate, ct)) return candidate;
        }
    }
}
