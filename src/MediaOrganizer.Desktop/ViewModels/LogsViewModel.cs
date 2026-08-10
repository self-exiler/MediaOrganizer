using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrganizer.Core.Logging;

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
        Dispatcher.UIThread.Post(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > _logger.Capacity) Entries.RemoveAt(0);
        });
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logger.Clear();
        Entries.Clear();
    }
}
