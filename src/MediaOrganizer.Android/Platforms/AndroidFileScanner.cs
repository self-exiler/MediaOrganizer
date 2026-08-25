using Android.Content;
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Scanning;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 自适应扫描器：sourceDir 为绝对路径时直接走 Core FileScanner（System.IO 枚举，
/// 依赖全局存储权限）；content: 树 URI 时经 DocumentFile.ListFiles() 递归（SAF 回退）。
/// 两种模式都按扩展名白名单过滤（FR-A1.2/A1.3），名称/大小/mtime 同步预取（R-A2）。
/// </summary>
public sealed class AndroidFileScanner : IFileScanner
{
    private readonly string[] _formats;
    private readonly bool _scanAllFiles;
    private readonly ContentResolver _resolver;
    private readonly FileScanner _pathScanner;

    public AndroidFileScanner(IEnumerable<string> supportedFormats, bool scanAllFiles, ContentResolver resolver)
    {
        _formats = supportedFormats
            .Select(f => f.TrimStart('.').ToLowerInvariant())
            .Where(f => f.Length > 0)
            .ToArray();
        _scanAllFiles = scanAllFiles;
        _resolver = resolver;
        _pathScanner = new FileScanner(_formats, scanAllFiles);
    }

    public IReadOnlyList<MediaFile> Scan(string sourceDir)
    {
        if (string.IsNullOrEmpty(sourceDir)) return [];

        // 全局存储已授权时优先把 content: 树 URI 换算为真实路径直读：
        // 治愈旧配置残留 SAF 标识 / 重装后持久授权丢失的场景
        if (SafPaths.IsSafIdentifier(sourceDir) && AndroidStorageAccess.HasAllFilesAccess
            && SafPaths.TryTreeUriToPath(sourceDir) is { } realPath)
            sourceDir = realPath;

        if (!SafPaths.IsSafIdentifier(sourceDir))
        {
            if (!Directory.Exists(sourceDir))
                throw new IOException($"源目录不存在或无权访问：{sourceDir}（请检查「所有文件访问」权限）");
            return _pathScanner.Scan(sourceDir);
        }

        var context = global::Android.App.Application.Context;
        var treeUri = global::Android.Net.Uri.Parse(sourceDir);
        var tree = treeUri is null ? null : DocumentFile.FromTreeUri(context, treeUri);
        if (tree is null || !tree.CanRead())
            throw new IOException($"源目录不可读（SAF 授权可能已失效）：{sourceDir}");

        var list = new List<MediaFile>();
        Walk(tree, list);
        return list;
    }

    private void Walk(DocumentFile dir, List<MediaFile> list)
    {
        foreach (var doc in dir.ListFiles())
        {
            if (doc.IsDirectory)
            {
                Walk(doc, list);
                continue;
            }
            var name = doc.Name ?? "";
            var ext = System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
            if (!_scanAllFiles && _formats.Length > 0 && !_formats.Contains(ext)) continue;
            try
            {
                var source = new AndroidSafMediaSource(doc, _resolver);
                list.Add(new MediaFile(source.Identifier, source.Length, ext) { Source = source });
            }
            catch
            {
                // 文件被占用/删除/URI 失效：跳过单个文件（NFR-A4）
            }
        }
    }
}
