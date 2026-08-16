using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Sources;

namespace MediaOrganizer.Core.Scanning;

/// <summary>递归扫描源目录，按扩展名白名单过滤；ScanAllFiles 或空白名单 = 全收（SRS FR-1）。</summary>
public sealed class FileScanner : IFileScanner
{
    private readonly HashSet<string> _formats;
    private readonly bool _scanAllFiles;

    public FileScanner(IEnumerable<string> supportedFormats, bool scanAllFiles = false)
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

        var dirInfo = new DirectoryInfo(sourceDir);
        foreach (var fi in dirInfo.EnumerateFiles("*", options))
        {
            var ext = fi.Extension.TrimStart('.').ToLowerInvariant();
            if (!_scanAllFiles && _formats.Count > 0 && !_formats.Contains(ext)) continue;
            try
            {
                list.Add(new MediaFile(fi.FullName, fi.Length, ext) { Source = new LocalMediaSource(fi.FullName) });
            }
            catch
            {
                // 文件可能在扫描过程中被占用/删除，跳过
            }
        }
        return list;
    }
}
