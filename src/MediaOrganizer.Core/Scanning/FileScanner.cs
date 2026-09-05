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
            // 仅跳过 ReparsePoint（符号链接/交接点）以避免递归死循环；System/Hidden 不再跳过（P2-11）：
            // FR-1.1「递归扫描所有文件」要求隐藏文件也应参与归档，此前静默丢弃且无可关配置。
            AttributesToSkip = FileAttributes.ReparsePoint
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
            catch (Exception ex)
            {
                // 文件可能在扫描过程中被占用/删除，跳过
                System.Diagnostics.Debug.WriteLine($"[FileScanner] Skipped file {fi.FullName}: {ex.Message}");
            }
        }
        return list;
    }
}
