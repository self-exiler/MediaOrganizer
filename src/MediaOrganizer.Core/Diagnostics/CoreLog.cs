namespace MediaOrganizer.Core.Diagnostics;

/// <summary>
/// Core 层静态日志出口：Core 内部（SmbFileStorage 连接诊断、传输埋点等）没有 AppLogger 注入通道，
/// 经此静态 Sink 转发到组合根的落盘日志；未接线时静默丢弃（单测/桌面未接线场景）。
/// </summary>
public static class CoreLog
{
    public static Action<string>? Sink { get; set; }

    public static void Info(string message) => Sink?.Invoke(message);

    public static void Warn(string message) => Sink?.Invoke(message);
}
