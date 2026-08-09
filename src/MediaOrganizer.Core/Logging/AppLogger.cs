using System.Collections.Concurrent;

namespace MediaOrganizer.Core.Logging;

public enum LogLevel { Info, Warn, Error }

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message);

/// <summary>内存环形日志（上限可配，默认 1000 条）。GUI 订阅 EntryAdded 实时刷新。</summary>
public sealed class AppLogger(int capacity = 1000)
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public event Action<LogEntry>? EntryAdded;

    public int Capacity { get; } = capacity;

    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warn(string message) => Log(LogLevel.Warn, message);
    public void Error(string message) => Log(LogLevel.Error, message);

    public void Log(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }
        EntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<LogEntry> Snapshot() => _entries.ToArray();

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
    }
}
