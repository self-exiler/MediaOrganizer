using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Core.Scanning;

/// <summary>递归扫描源目录，按扩展名白名单过滤（SRS FR-1）。</summary>
public sealed class FileScanner
{
    private readonly HashSet<string> _formats;
    private readonly bool _scanAllFiles;

    public FileScanner(IEnumerable<string> supportedFormats, bool scanAllFiles)
    {
        _formats = supportedFormats.Select(f => f.TrimStart('.').ToLowerInvariant()).ToHashSet();
        _scanAllFiles = scanAllFiles;
    }

    public static FileScanner FromConfig(Configuration.AppConfig config)
        => new(config.Scan.SupportedFormats, config.Scan.ScanAllFiles);

    public IReadOnlyList<MediaFile> Scan(string sourceDir)
    {
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            return [];

        var list = new List<MediaFile>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden
        };

        foreach (var path in Directory.EnumerateFiles(sourceDir, "*", options))
        {
            var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (!_scanAllFiles && !_formats.Contains(ext)) continue;
            try
            {
                list.Add(new MediaFile(path, new FileInfo(path).Length, ext));
            }
            catch
            {
                // 文件可能在扫描过程中被占用/删除，跳过
            }
        }
        return list;
    }
}
