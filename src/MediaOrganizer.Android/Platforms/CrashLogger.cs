namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 未捕获异常落盘：Honor 等机型对非系统应用加密 logcat（adb 只能看到 HKS…HKE 密文块），
/// 崩溃现场无法从系统日志读取，故自建文件日志（私有目录，run-as 可拉取）。
/// 钩子在 Application.OnCreate start（Avalonia Android 入口，早于活动页面初始化），覆盖 OnCreate 及之后的托管异常。
/// </summary>
public static class CrashLogger
{
    private static string? _dir;
    private static bool _installed;

    public static void Install(global::Android.Content.Context context)
    {
        if (_installed) return;
        _installed = true;
        try
        {
            _dir = context.FilesDir?.AbsolutePath;
            AppDomain.CurrentDomain.UnhandledException += (_, e)
                => Write("UnhandledException", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e)
                => Write("UnobservedTaskException", e.Exception);
        }
        catch
        {
            // 日志器自身故障不得影响应用
        }
    }

    private const long MaxFileSize = 256 * 1024;

    private static void Write(string kind, Exception? ex, bool stackTrace = true)
    {
        try
        {
            if (_dir is null) return;
            var file = Path.Combine(_dir, "crash.txt");
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {kind}\n"
                       + (ex is null ? "(no exception object)" : Format(ex, stackTrace))
                       + "\n";
            // 防无限增长：超过上限时丢弃旧内容只留最新一条（现场日志仅用于排障，不需完整历史）
            if (File.Exists(file) && new FileInfo(file).Length > MaxFileSize)
                File.WriteAllText(file, text);
            else
                File.AppendAllText(file, text);
        }
        catch
        {
            // 落盘失败静默
        }
    }

    private static string Format(Exception ex, bool stackTrace)
    {
        var sb = new System.Text.StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            sb.Append(e.GetType().FullName).Append(": ").AppendLine(e.Message);
            if (stackTrace && e.StackTrace is { } st) sb.AppendLine(st);
        }
        return sb.ToString();
    }
}
