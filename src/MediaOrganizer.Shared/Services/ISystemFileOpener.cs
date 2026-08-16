namespace MediaOrganizer.Shared.Services;

/// <summary>
/// 用系统默认应用打开文件（FR-A6.3）：桌面 Launcher.LaunchFileInfoAsync；
/// Android ACTION_VIEW Intent（content URI + FLAG_GRANT_READ_URI_PERMISSION）。
/// </summary>
public interface ISystemFileOpener
{
    Task OpenAsync(string identifier);
}
