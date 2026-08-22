using Android.Content;
using Android.OS;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 分析/执行期间持有 PARTIAL_WAKE_LOCK 防息屏中断（NFR-A9，FR-A9.7）：
/// 非引用计数 + 4 小时安全超时（防异常路径泄漏）；完成即释放。
/// </summary>
public sealed class WakeLockHolder : IDisposable
{
    private PowerManager.WakeLock? _lock;
    private static readonly long TimeoutMillis = 4L * 60 * 60 * 1000;

    public void Acquire()
    {
        if (_lock?.IsHeld == true) return;
        var pm = (PowerManager?)global::Android.App.Application.Context.GetSystemService(Context.PowerService);
        _lock = pm?.NewWakeLock(WakeLockFlags.Partial, "mediaorganizer:organize");
        _lock?.SetReferenceCounted(false);
        _lock?.Acquire(TimeoutMillis);
    }

    public void Release()
    {
        try
        {
            if (_lock?.IsHeld == true) _lock.Release();
        }
        catch
        {
            // 释放失败不影响主流程
        }
        _lock = null;
    }

    public void Dispose() => Release();
}
