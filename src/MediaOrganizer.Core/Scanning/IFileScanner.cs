using MediaOrganizer.Core.Models;

namespace MediaOrganizer.Core.Scanning;

/// <summary>
/// 源目录扫描抽象（ADR-0006 背景点 4）：Analyzer 面向接口，
/// 桌面注入 FileScanner（本地路径），Android 注入 AndroidFileScanner（SAF 树）。
/// </summary>
public interface IFileScanner
{
    /// <summary>扫描源目录（本地路径或 SAF 树 URI），返回带源端抽象的媒体文件列表。</summary>
    IReadOnlyList<MediaFile> Scan(string sourceDir);
}
