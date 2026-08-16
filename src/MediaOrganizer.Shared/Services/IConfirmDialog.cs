namespace MediaOrganizer.Shared.Services;

/// <summary>确认对话框抽象：桌面模态 Window（ConfirmDialog）；Android AlertDialog。</summary>
public interface IConfirmDialog
{
    Task<bool> ConfirmAsync(string title, string message);
}
