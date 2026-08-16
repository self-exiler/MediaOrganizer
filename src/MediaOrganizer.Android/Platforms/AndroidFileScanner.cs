using Android.Content;
using AndroidX.DocumentFile.Provider;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Scanning;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// SAF 树递归扫描器（FR-A1，ADR-0005 §3）：sourceDir 为 ACTION_OPEN_DOCUMENT_TREE 返回的树 URI 字符串。
/// 遍历 DocumentFile.listFiles() 替代 Directory.EnumerateFiles，按扩展名白名单过滤；
/// 每个文件构造 AndroidSafMediaSource（预取名称/大小/mtime，mtime 兜底零额外查询）。
/// </summary>
public sealed class AndroidFileScanner : IFileScanner
{
    private readonly HashSet<string> _formats;
    private readonly bool _scanAllFiles;
    private readonly ContentResolver _resolver;

    public AndroidFileScanner(IEnumerable<string> supportedFormats, bool scanAllFiles = false, ContentResolver? resolver = null)
    {
        _formats = supportedFormats.Select(f => f.TrimStart('.').ToLowerInvariant()).ToHashSet();
        _scanAllFiles = scanAllFiles;
        _resolver = resolver ?? global::Android.App.Application.Context.ContentResolver;
    }

    public static AndroidFileScanner FromConfig(MediaOrganizer.Core.Configuration.AppConfig config)
        => new(config.Scan.SupportedFormats, config.Scan.ScanAllFiles);

    public IReadOnlyList<MediaFile> Scan(string sourceDir)
    {
        var treeUri = global::Android.Net.Uri.Parse(sourceDir);
        if (treeUri is null) return [];

        var tree = DocumentFile.FromTreeUri(global::Android.App.Application.Context, treeUri);
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
                // 文件可能被占用/删除，跳过
            }
        }
    }
}