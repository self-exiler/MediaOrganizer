using Avalonia.Platform.Storage;
using MediaOrganizer.Desktop.Views;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Desktop.Services;

/// <summary>桌面目录选取：StorageProvider 文件夹对话框（ADR-0006 决策 4）。</summary>
public sealed class DesktopFolderPicker : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
        var top = App.MainWindow;
        if (top is null) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择目录",
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}

/// <summary>桌面文件另存为 / 分享：StorageProvider 对话框（分享降级为另存为）。</summary>
public sealed class DesktopFileSaver : IFileSaver
{
    public async Task<bool> SaveTextAsync(string suggestedName, string content)
    {
        var top = App.MainWindow;
        if (top is null) return false;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "另存分析报告",
            SuggestedFileName = suggestedName,
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType("文本文件") { Patterns = ["*.txt"] }]
        });
        if (file is null) return false;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
        return true;
    }

    public Task ShareTextAsync(string title, string content)
        => SaveTextAsync("analysis-report.txt", content);
}

/// <summary>桌面确认弹窗：包装 ConfirmDialog 模态窗口。</summary>
public sealed class DesktopConfirmDialog : IConfirmDialog
{
    public Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = App.MainWindow;
        return owner is null
            ? Task.FromResult(true)
            : ConfirmDialog.AskAsync(owner, title, message);
    }
}

/// <summary>桌面系统打开：Launcher.LaunchFileInfoAsync。</summary>
public sealed class DesktopSystemFileOpener : ISystemFileOpener
{
    public async Task OpenAsync(string identifier)
    {
        var top = App.MainWindow;
        if (top is null) return;
        try
        {
            await top.Launcher.LaunchFileInfoAsync(new FileInfo(identifier));
        }
        catch
        {
            // 忽略打开失败
        }
    }
}

/// <summary>桌面缩略图：包装 ThumbnailService（Magick.NET + LRU 缓存）。</summary>
public sealed class DesktopImageLoader : IImageLoader
{
    public Avalonia.Media.Imaging.Bitmap? LoadThumbnail(string identifier, int maxSize)
        => ThumbnailService.Load(identifier, maxSize);
}
