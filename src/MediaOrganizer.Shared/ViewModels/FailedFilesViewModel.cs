using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Execution;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Patterns;
using MediaOrganizer.Shared.Services;
using System.Collections.ObjectModel;

namespace MediaOrganizer.Shared.ViewModels;

/// <summary>失败文件列表项。IsChecked 为批量移动的目标选择（Android 触屏勾选；桌面单选不受影响）。</summary>
public partial class FailedItem : ObservableObject
{
    public FailedItem(UnparsedFile file)
    {
        File = file;
        Fingerprint = StructureFingerprint.Compute(file.File.FileName);
    }

    public UnparsedFile File { get; }
    public string Name => File.File.FileName;
    public string Path => File.File.Path;
    public long Size => File.File.Size;
    public string Fingerprint { get; }
    public string Reason => File.Reason;

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>
/// 失败文件：列表 + 预览 + 批量移动 + 打开系统应用（双端共享，ADR-0006 决策 4/5）。
/// 批量移动下沉 Core PendingFileMover（流式复制 + 删除源，SAF/本地统一语义）；
/// 确认弹窗 / 系统打开 / 缩略图预览经注入抽象（桌面实现齐全；Android 无预览、其余走系统 Intent）。
/// </summary>
public partial class FailedFilesViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly AppLogger _logger;
    private readonly AppState _state;
    private readonly IConfirmDialog _confirm;
    private readonly ISystemFileOpener _opener;
    private readonly IImageLoader _imageLoader;

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

    public FailedFilesViewModel(
        AppState state,
        AppLogger logger,
        IConfirmDialog confirm,
        ISystemFileOpener opener,
        IImageLoader imageLoader)
    {
        _state = state;
        _config = state.Config;
        _logger = logger;
        _confirm = confirm;
        _opener = opener;
        _imageLoader = imageLoader;
        _pendingDir = state.Config.Paths.PendingDir;
    }

    public void Refresh(AnalysisResult? result)
    {
        Items.Clear();
        Preview = null;
        if (result is null) return;
        // FR-A6.1：按结构指纹聚类排序（指纹一致的相邻）
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
        var bmp = await Task.Run(() => _imageLoader.LoadThumbnail(item.Path, _config.General.PreviewSize));
        if (ReferenceEquals(Selected, item) && bmp is not null)
        {
            Preview = bmp;
            PreviewInfo = $"{item.Name}\n{item.Size:N0} 字节";
        }
    }

    [RelayCommand]
    private async Task OpenWithSystem(FailedItem? item = null)
    {
        var target = item ?? Selected;
        if (target is null) return;
        await _opener.OpenAsync(target.Path);
    }

    /// <summary>全选/取消全选（Android 触屏批量操作）：有未勾选项时全部勾选，否则全部取消。</summary>
    [RelayCommand]
    private void ToggleSelectAll()
    {
        var check = Items.Any(i => !i.IsChecked);
        foreach (var i in Items)
            i.IsChecked = check;
    }

    [RelayCommand]
    private void OpenMagicTools()
    {
        // 桌面端接线导航到魔术工具；Android 无魔术工具，不订阅此事件（FR-A6.4）
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

        // 有勾选项时仅移动勾选的（Android 批量选择语义）；全部未勾选则移动全部（桌面语义保持不变）
        var items = Items.Where(i => i.IsChecked).ToArray();
        if (items.Length == 0) items = Items.ToArray();

        // FR-A6.5：目标目录必须由用户指定，不自动落盘兜底
        if (string.IsNullOrWhiteSpace(PendingDir))
        {
            MoveHint = "请先指定「待处理目录」";
            return;
        }
        var targetDir = PendingDir;

        // FR-A6.5：移动前确认（SAF 移动 = 复制 + 删除源）
        var confirmed = await _confirm.ConfirmAsync(
            "移入待处理目录",
            $"将把 {items.Length} 个文件移动到：\n{targetDir}\n\n（移动 = 复制 + 删除源）确定继续吗？");
        if (!confirmed) return;

        IsMoving = true;
        MoveProgress = 0;
        MoveHint = "";
        try
        {
            var target = StorageFactory.CreateLocalStorage(targetDir);
            var progress = new Progress<double>(p => MoveProgress = p * 100);
            var result = await new PendingFileMover()
                .MoveAsync(items.Select(i => i.File.File).ToArray(), target, progress);

            // 从列表移除已移动的条目
            foreach (var item in items.Where(i => result.MovedNames.Contains(i.Name)))
                Items.Remove(item);

            MoveHint = $"移动完成：成功 {result.Moved}，失败 {result.Failed}";
            _logger.Info(MoveHint);
            foreach (var err in result.Errors.Take(10))
                _logger.Warn(err);
        }
        catch (Exception ex)
        {
            MoveHint = $"移动失败：{ex.Message}";
            _logger.Error(MoveHint);
        }
        finally
        {
            IsMoving = false;
        }
    }
}
