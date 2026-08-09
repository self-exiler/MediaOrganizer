using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Patterns;
using MediaOrganizer.Desktop.Services;

namespace MediaOrganizer.Desktop.ViewModels;

public sealed record FailedItem(UnparsedFile File)
{
    public string Name => File.File.FileName;
    public string Path => File.File.Path;
    public string Fingerprint => StructureFingerprint.Compute(File.File.FileName);
    public string Reason => File.Reason;
}

/// <summary>失败文件：列表 + 预览 + 批量移动 + 打开系统应用/魔术工具。</summary>
public partial class FailedFilesViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly AppLogger _logger;

    public event Action<IEnumerable<string>>? NavigateToMagic;

    public ObservableCollection<FailedItem> Items { get; } = [];

    [ObservableProperty]
    private FailedItem? _selected;

    [ObservableProperty]
    private Bitmap? _preview;

    [ObservableProperty]
    private string _previewInfo = "选择文件查看预览";

    [ObservableProperty]
    private string _pendingDir;

    public FailedFilesViewModel(AppConfig config, AppLogger logger)
    {
        _config = config;
        _logger = logger;
        _pendingDir = config.Paths.PendingDir;
    }

    public void Refresh(AnalysisResult? result)
    {
        Items.Clear();
        Preview = null;
        if (result is null) return;
        foreach (var u in result.Unparsed)
            Items.Add(new FailedItem(u));
        _logger.Info($"失败文件列表已刷新：{Items.Count} 个");
    }

    partial void OnSelectedChanged(FailedItem? value)
    {
        Preview = null;
        PreviewInfo = value is null ? "选择文件查看预览" : value.Path;
        if (value is null) return;
        _ = LoadPreviewAsync(value);
    }

    private async Task LoadPreviewAsync(FailedItem item)
    {
        var bmp = await Task.Run(() => ThumbnailService.Load(item.Path, _config.General.PreviewSize));
        if (ReferenceEquals(Selected, item) && bmp is not null)
        {
            Preview = bmp;
            PreviewInfo = $"{item.Name}\n{item.File.File.Size:N0} 字节";
        }
    }

    [RelayCommand]
    private void OpenWithSystem()
    {
        if (Selected is null) return;
        ThumbnailService.OpenWithSystemApp(Selected.Path);
    }

    [RelayCommand]
    private void OpenMagicTools()
    {
        var names = (Selected is not null
                ? new[] { Selected.Name }
                : Items.Select(i => i.Name))
            .Distinct()
            .Take(50)
            .ToArray();
        if (names.Length > 0)
            NavigateToMagic?.Invoke(names);
    }

    [RelayCommand]
    private void MoveToPending()
    {
        if (Items.Count == 0) return;

        var targetDir = string.IsNullOrWhiteSpace(PendingDir)
            ? Path.Combine(_config.Paths.OutputDir, "待处理")
            : PendingDir;
        Directory.CreateDirectory(targetDir);

        int moved = 0, failed = 0;
        foreach (var item in Items.ToArray())
        {
            try
            {
                var target = Path.Combine(targetDir, item.Name);
                if (File.Exists(target))
                {
                    var name = Path.GetFileNameWithoutExtension(target);
                    var ext = Path.GetExtension(target);
                    for (var i = 1; File.Exists(target); i++)
                        target = Path.Combine(targetDir, $"{name}_{i}{ext}");
                }
                File.Move(item.Path, target);
                Items.Remove(item);
                moved++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.Warn($"{item.Name} 移动失败：{ex.Message}");
            }
        }
        _config.Paths.PendingDir = targetDir;
        _logger.Info($"批量移动完成：成功 {moved}，失败 {failed}");
    }
}
