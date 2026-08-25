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

        var dir = relativePath[..relativePath.LastIndexOf('/')];
        var name = Path.GetFileNameWithoutExtension(relativePath);
        var ext = Path.GetExtension(relativePath);
        for (var i = 1; ; i++)
        {
            var candidate = dir.Length == 0 ? $"{name}_{i}{ext}" : $"{dir}/{name}_{i}{ext}";
            if (!await target.ExistsAsync(candidate, ct)) return candidate;
        }
    }

    /// <summary>异步查重名（仅文件名，无目录前缀）：用于 PendingFileMover 等不带目录的场景。</summary>
    public static async Task<string> FindFreeNameOnlyAsync(
        IFileStorage target, string name, CancellationToken ct)
    {
        if (!await target.ExistsAsync(name, ct)) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 1; ; i++)
        {
            var candidate = $"{stem}_{i}{ext}";
            if (!await target.ExistsAsync(candidate, ct)) return candidate;
        }
    }

    /// <summary>本地文件系统查重名：返回追加 _n 且不存在的目标路径（同步，用 File.Exists）。</summary>
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
