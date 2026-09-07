using Android.Content;
using Android.Net.Wifi;
using Android.OS;
using Avalonia.Threading;
using MediaOrganizer.Core;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Diagnostics;
using MediaOrganizer.Core.Execution;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 进程级任务宿主（IJobHost 的 Android 实现）：持有运行中分析/执行任务的取消令牌，
/// 经 dataSync 前台服务保活——退后台/锁屏后进程为前台优先级，网络不受 Doze 限制。
/// Start* 必须在 UI 线程调用：宿主内 new Progress&lt;T&gt; 与 OrganizeSession 的事件
/// （AnalysisCompleted/PlanChanged → VM 改 ObservableProperty）依赖 Avalonia 同步上下文回 UI 线程，
/// 因此不额外 Task.Run 包裹 session 调用（session 内部已自行 Task.Run/并行化）。
/// 回调一律经 Dispatcher.UIThread.Post 保证在 UI 线程执行。
/// </summary>
public sealed class OrganizeJobHost : IJobHost
{
    public static OrganizeJobHost? Current { get; internal set; }

    private readonly IConfirmDialog _confirm;
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private bool _running;
    private volatile bool _systemTimeout;
    private volatile string _lastOutcome = "";
    private bool _batteryHintShown;
    private JobKind _kind;
    private DateTimeOffset _startedAt;
    // WifiLock 是 WifiManager 的嵌套类（Java: WifiManager.WifiLock），不是 Android.Net.Wifi 下的顶层类型。
    // 另：WifiMode 位于 Android.Net（而非 Android.Net.Wifi），本文件只 using 了后者，故一律 global:: 全限定。
    private global::Android.Net.Wifi.WifiManager.WifiLock? _wifiLock;

    // 通知消费的进度快照（跨线程读写；double 不能标 volatile，展示值无强一致需求，
    // Snapshot 侧已在锁内读取，写侧单字段赋值即可）
    private volatile string _phaseText = "";
    private double _progressFraction = -1; // <0 = 不定进度
    private long _lastNotifyTicks;

    public bool IsJobRunning
    {
        get { lock (_sync) return _running; }
    }

    /// <summary>进度快照变化（500ms 节流），前台服务订阅以刷新通知。</summary>
    public event Action? StateChanged;

    /// <summary>任务终结（完成/取消/失败/系统超时），参数为结局摘要；服务据此发最终通知并停止自身。</summary>
    public event Action<string>? Finished;

    public OrganizeJobHost(IConfirmDialog confirm) => _confirm = confirm;

    private enum JobKind { Analyze, Execute }

    public void StartAnalysis(OrganizeSession session, string sourceDir, string outputRoot,
        IProgress<AnalysisProgress> progress,
        Action onCompleted, Action<Exception> onFailed, Action onCanceled)
    {
        if (!Begin(JobKind.Analyze, onFailed)) return;
        var observed = new Progress<AnalysisProgress>(p =>
        {
            _phaseText = $"已处理 {p.Processed}/{p.Total}";
            _progressFraction = p.Total == 0 ? -1 : (double)p.Processed / p.Total;
            RaiseStateChanged();
            progress.Report(p);
        });
        var analyzeCt = BeginToken();
        _ = RunJobAsync(onCompleted, onFailed, onCanceled,
            () => session.AnalyzeAsync(sourceDir, outputRoot, observed, analyzeCt),
            _ => "分析完成，回应用查看结果");
    }

    public void StartExecution(OrganizeSession session, IProgress<double> progress,
        Action<FileOperationResult> onCompleted, Action<Exception> onFailed, Action onCanceled)
    {
        if (!Begin(JobKind.Execute, onFailed)) return;
        var observed = new Progress<double>(p =>
        {
            _phaseText = $"进度 {p:P0}";
            _progressFraction = p;
            RaiseStateChanged();
            progress.Report(p);
        });
        var executeCt = BeginToken();
        _ = RunJobAsync(onCompleted, onFailed, onCanceled,
            () => session.ExecuteAsync(observed, executeCt),
            result => JobText.DescribeExecution(result));
    }

    public void Cancel()
    {
        lock (_sync) _cts?.Cancel();
    }

    /// <summary>系统 dataSync 配额触顶（服务 OnTimeout 回调）：取消任务并在完成通知中说明原因。</summary>
    public void CancelBySystemTimeout()
    {
        _systemTimeout = true;
        Cancel();
    }

    /// <summary>通知渲染用的状态快照（服务线程读取）。</summary>
    public (string Title, string Detail, double? Fraction) Snapshot()
    {
        lock (_sync)
        {
            if (!_running) return ("MediaOrganizer", "", null);
            var elapsed = DateTimeOffset.Now - _startedAt;
            var elapsedText = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}小时{elapsed.Minutes}分{elapsed.Seconds}秒"
                : $"{(int)elapsed.TotalMinutes}分{elapsed.Seconds}秒";
            var title = _kind == JobKind.Analyze ? "MediaOrganizer 分析中" : "MediaOrganizer 备份中";
            return (title, $"{_phaseText} · 已用 {elapsedText}", _progressFraction);
        }
    }

    private bool Begin(JobKind kind, Action<Exception> onFailed)
    {
        lock (_sync)
        {
            if (_running)
            {
                Post(() => onFailed(new InvalidOperationException("已有任务在运行")));
                return false;
            }
            _running = true;
            _systemTimeout = false;
            _kind = kind;
            _startedAt = DateTimeOffset.Now;
        }
        _phaseText = kind == JobKind.Analyze ? "正在扫描分析…" : "正在启动备份…";
        _progressFraction = -1;
        AcquireWifiLock();
        StartForegroundService();
        MaybeHintBatteryWhitelist();
        RaiseStateChanged(force: true);
        return true;
    }

    private CancellationToken BeginToken()
    {
        lock (_sync)
        {
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }
    }

    private async Task RunJobAsync<T>(
        Action<T> onCompleted, Action<Exception> onFailed, Action onCanceled,
        Func<Task<T>> job, Func<T, string> describe)
    {
        try
        {
            var result = await job();
            _phaseText = "已完成";
            _lastOutcome = describe(result);
            Post(() => onCompleted(result));
        }
        catch (OperationCanceledException)
        {
            _lastOutcome = _systemTimeout ? "已达系统后台时长上限，任务已停止" : "任务已取消，已完成文件保留";
            Post(onCanceled);
        }
        catch (Exception ex)
        {
            _lastOutcome = $"任务失败：{ex.Message}";
            Post(() => onFailed(ex));
        }
        finally
        {
            End();
        }
    }

    private void End()
    {
        string outcome;
        lock (_sync)
        {
            _running = false;
            outcome = _lastOutcome;
            _cts?.Dispose();
            _cts = null;
        }
        ReleaseWifiLock();
        _progressFraction = -1;
        RaiseStateChanged(force: true);
        Finished?.Invoke(outcome);
    }

    private void StartForegroundService()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            context.StartForegroundService(new Intent(context, typeof(OrganizeForegroundService)));
        }
        catch (Exception ex)
        {
            // FGS 启动失败（后台启动限制/ROM 拦截）：不阻断任务，退化为旧行为（退后台可能被回收）
            CoreLog.Warn($"[JobHost] 前台服务启动失败，任务继续但退后台可能被系统回收：{ex.Message}");
        }
    }

    /// <summary>一次性引导：未加入电池优化白名单时提示（荣耀/华为等 ROM 锁屏后仍可能杀前台服务）。</summary>
    private void MaybeHintBatteryWhitelist()
    {
        if (_batteryHintShown) return;
        _batteryHintShown = true;
        try
        {
            var context = global::Android.App.Application.Context;
            var pm = (PowerManager?)context.GetSystemService(Context.PowerService);
            if (pm is null || pm.IsIgnoringBatteryOptimizations(context.PackageName)) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500); // 让任务先启动，提示不阻塞、不抢焦点
                var go = await _confirm.ConfirmAsync("后台备份稳定性建议",
                    "本机未将 MediaOrganizer 加入电池优化白名单。锁屏后部分系统（荣耀/华为等）仍可能终止备份。\n\n是否打开系统设置进行配置？");
                if (!go) return;
                var activity = MainActivity.Instance;
                if (activity is null) return;
                activity.RunOnUiThread(() =>
                {
                    try
                    {
                        activity.StartActivity(new Intent(global::Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings));
                    }
                    catch (Exception ex)
                    {
                        CoreLog.Warn($"[JobHost] 打开电池优化设置失败：{ex.Message}");
                    }
                });
            });
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"[JobHost] 电池优化检查失败：{ex.Message}");
        }
    }

    private void AcquireWifiLock()
    {
        try
        {
            var wifi = (global::Android.Net.Wifi.WifiManager?)global::Android.App.Application.Context.GetSystemService(Context.WifiService);
            if (wifi is null) return;
            var @lock = wifi.CreateWifiLock(global::Android.Net.WifiMode.FullHighPerf, "mediaorganizer:wifi");
            if (@lock is null) return;
            @lock.SetReferenceCounted(false);
            @lock.Acquire();
            _wifiLock = @lock;
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"[JobHost] WifiLock 获取失败：{ex.Message}");
        }
    }

    private void ReleaseWifiLock()
    {
        try { _wifiLock?.Release(); }
        catch { /* 已自动释放等场景 */ }
        _wifiLock = null;
    }

    /// <summary>UI 线程投递（回调契约：VM 的 ObservableProperty 只在 UI 线程变更）。</summary>
    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    private void RaiseStateChanged(bool force = false)
    {
        if (!force)
        {
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastNotifyTicks);
            if (now - last < 500) return;
            Interlocked.Exchange(ref _lastNotifyTicks, now);
        }
        StateChanged?.Invoke();
    }
}
