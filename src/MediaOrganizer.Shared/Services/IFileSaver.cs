namespace MediaOrganizer.Shared.Services;

/// <summary>
/// 文件导出抽象（ADR-0006 决策 6）：桌面 StorageProvider 另存为对话框；
/// Android SAF ACTION_CREATE_DOCUMENT 或系统分享（FR-A 报告页「另存为/分享」）。
/// </summary>
public interface IFileSaver
{
    /// <summary>另存文本文件（用户选择位置）；返回是否保存成功。</summary>
    Task<bool> SaveTextAsync(string suggestedName, string content);

    /// <summary>系统分享文本内容（Android 分享面板；桌面降级为另存为）。</summary>
    Task ShareTextAsync(string title, string content);
}
