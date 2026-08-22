using Android.Content;
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Scanning;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// SAF 树递归扫描器（FR-A1）：sourceDir 为 ACTION_OPEN_DOCUMENT_TREE 返回的树 URI 字符串。
/// 遍历 DocumentFile.ListFiles() 替代 Directory.EnumerateFiles，按扩展名白名单过滤（FR-A1.2/A1.3）；
/// 每个文件构造 AndroidSafMediaSource，名称/大小/mtime 随 DocumentFile 预取（R-A2）。
/// </summary>
public sealed class AndroidFileScanner : IFileScanner
{
    private readonly HashSet<string> _formats;
    private readonly bool _scanAllFiles;
    private readonly ContentResolver _resolver;

    public AndroidFileScanner(IEnumerable<string> supportedFormats, bool scanAllFiles, ContentResolver resolver)
    {
        _formats = supportedFormats
            .Select(f => f.TrimStart('.').ToLowerInvariant())
            .Where(f => f.Length > 0)
            .ToHashSet();
        _scanAllFiles = scanAllFiles;
        _resolver = resolver;
    }

    public IReadOnlyList<MediaFile> Scan(string sourceDir)
    {
        if (string.IsNullOrEmpty(sourceDir)) return [];

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
            if (!_scanAllFiles && _formats.Count > 0 && !_formats.Contains(ext)) continue;
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
