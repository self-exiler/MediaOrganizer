namespace MediaOrganizer.Shared.Services;

/// <summary>
/// 目录选取抽象（ADR-0006 决策 4）：桌面 StorageProvider 文件夹对话框；
/// Android SAF ACTION_OPEN_DOCUMENT_TREE（返回树 URI 字符串）。
/// </summary>
public interface IFolderPicker
{
    /// <summary>选取目录；用户取消返回 null。返回值为源标识符（本地路径或 content:// URI）。</summary>
    Task<string?> PickFolderAsync();
}
