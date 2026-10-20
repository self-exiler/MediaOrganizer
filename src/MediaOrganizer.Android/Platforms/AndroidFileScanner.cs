using Android.Content;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Scanning;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 自适应扫描器：sourceDir 为绝对路径时直接走 Core FileScanner（System.IO 枚举，依赖全局存储权限）；
/// content: 树 URI 先尽力换算为真实路径（治愈旧配置残留/重装后持久授权丢失），
/// 换算不了即报错——SAF 递归回退已删除（2026-10 精简决策，该路径从未支撑归档）。
/// </summary>
public sealed class AndroidFileScanner : IFileScanner
{
    private readonly FileScanner _pathScanner;

    public AndroidFileScanner(IEnumerable<string> supportedFormats, bool scanAllFiles, ContentResolver resolver)
    {
        // HashSet(OrdinalIgnoreCase) 替代数组 Contains 的逐文件 O(n) 线性查，且免规范化分配
        var formats = supportedFormats
            .Select(f => f.TrimStart('.'))
            .Where(f => f.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _pathScanner = new FileScanner(formats, scanAllFiles);
    }

    public IReadOnlyList<MediaFile> Scan(string sourceDir)
    {
        if (string.IsNullOrEmpty(sourceDir)) return [];

        if (SafPaths.IsSafIdentifier(sourceDir) && AndroidStorageAccess.HasAllFilesAccess
            && SafPaths.TryTreeUriToPath(sourceDir) is { } realPath)
            sourceDir = realPath;

        if (SafPaths.IsSafIdentifier(sourceDir))
            throw new IOException($"无法将所选 SAF 目录映射为真实路径：{sourceDir}（请授予「所有文件访问」权限后重新选择目录）");

        if (!Directory.Exists(sourceDir))
            throw new IOException($"源目录不存在或无权访问：{sourceDir}（请检查「所有文件访问」权限）");
        return _pathScanner.Scan(sourceDir);
    }
}
