using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Shared.ViewModels;
using System.Collections.ObjectModel;

namespace MediaOrganizer.Desktop.ViewModels;

/// <summary>日志视图：订阅 AppLogger，实时追加。</summary>
public partial class LogsViewModel : ViewModelBase
{
    private readonly AppLogger _logger;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public LogsViewModel(AppLogger logger)
    {
        _logger = logger;
        foreach (var e in logger.Snapshot()) Entries.Add(e);
        logger.EntryAdded += OnEntryAdded;
    }

    private void OnEntryAdded(LogEntry entry)
    {
        // perf-17：已在 UI 线程则直接追加（Post 会把通知排队到下一轮循环，高频日志时堆积延迟）
        if (Dispatcher.UIThread.CheckAccess()) Append(entry);
        else Dispatcher.UIThread.Post(() => Append(entry));
    }

    private void Append(LogEntry entry)
    {
        Entries.Add(entry);
        // 批量裁剪：RemoveAt(0) 是 O(n) 搬移，先算超出量一次做掉
        var excess = Entries.Count - _logger.Capacity;
        while (excess-- > 0) Entries.RemoveAt(0);
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logger.Clear();
        Entries.Clear();
    }
}
