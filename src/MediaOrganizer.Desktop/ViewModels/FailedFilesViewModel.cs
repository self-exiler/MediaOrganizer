using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Execution;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Patterns;
using MediaOrganizer.Desktop.Services;
using MediaOrganizer.Desktop.Views;

namespace MediaOrganizer.Desktop.ViewModels;

public sealed record FailedItem(UnparsedFile File)
{
    public string Name => File.File.FileName;
    public string Path => File.File.Path;
    public long Size => File.File.Size;
    public string Fingerprint { get; } = StructureFingerprint.Compute(File.File.FileName);
    public string Reason => File.Reason;
}

/// <summary>失败文件：列表 + 预览 + 批量移动 + 打开系统应用/魔术工具。</summary>
public partial class FailedFilesViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly AppLogger _logger;
    private readonly AppState _state;

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

    [ObservableProperty]
    private bool _isMoving;

    [ObservableProperty]
    private double _moveProgress;

    [ObservableProperty]
    private string _moveHint = "";

    public FailedFilesViewModel(AppState state, AppLogger logger)
    {
        _state = state;
        _config = state.Config;
        _logger = logger;
        _pendingDir = state.Config.Paths.PendingDir;
    }

    public void Refresh(AnalysisResult? result)
    {
        Items.Clear();
        Preview = null;
        if (result is null) return;
        // FR-6.1：按结构指纹聚类排序（指纹一致的相邻）
        foreach (var u in result.Unparsed
                     .OrderBy(f => StructureFingerprint.Compute(f.File.FileName))
                     .ThenBy(f => f.File.FileName))
            Items.Add(new FailedItem(u));
        _logger.Info($"失败文件列表已刷新：{Items.Count} 个（按指纹聚类排序）");
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
            PreviewInfo = $"{item.Name}\n{item.Size:N0} 字节";
        }
    }

    [RelayCommand]
    private async Task OpenWithSystem()
    {
        if (Selected is null) return;
        await ThumbnailService.OpenWithSystemAppAsync(Selected.Path);
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
    private async Task MoveToPending()
    {
        if (Items.Count == 0 || IsMoving) return;

        // FR-6.5：目标目录必须由用户指定，不自动落盘兜底
        if (string.IsNullOrWhiteSpace(PendingDir))
        {
            MoveHint = "请先在设置中指定「待处理文件夹」";
            return;
        }
        var targetDir = PendingDir;

        // FR-6.5：移动前确认
        var owner = App.MainWindow;
        var confirmed = owner is null || await ConfirmDialog.AskAsync(owner,
            "移入待处理文件夹",
            $"将把 {Items.Count} 个文件移动到：\n{targetDir}\n\n确定继续吗？");
        if (!confirmed) return;

        Directory.CreateDirectory(targetDir);
        IsMoving = true;
        MoveProgress = 0;
        MoveHint = "";
        int moved = 0, failed = 0;
        var items = Items.ToArray();
        try
        {
            await Task.Run(() =>
            {
                for (var i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    try
                    {
                        var target = Path.Combine(targetDir, item.Name);
                        if (File.Exists(target))
                            target = FileOperator.FindFreeLocalPath(target);
                        File.Move(item.Path, target);
                        moved++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.Warn($"{item.Name} 移动失败：{ex.Message}");
                    }
                    MoveProgress = (double)(i + 1) / items.Length;
                }
            });
            foreach (var item in items.Where(i => !File.Exists(i.Path))) Items.Remove(item);
            _state.Config.Paths.PendingDir = targetDir;
            MoveHint = $"移动完成：成功 {moved}，失败 {failed}";
            _logger.Info(MoveHint);
        }
        finally
        {
            IsMoving = false;
        }
    }
}
