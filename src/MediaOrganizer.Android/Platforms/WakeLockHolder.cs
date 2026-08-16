using Android.App;
using Android.Content;
using Android.OS;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// WakeLock 持有（NFR-A9）：分析/执行期间持 PARTIAL_WAKE_LOCK 防止息屏中断，完成后释放。
/// </summary>
public sealed class WakeLockHolder
{
    private PowerManager.WakeLock? _wakeLock;

    public void Acquire()
    {
        if (_wakeLock is not null) return;
        var pm = (PowerManager?)Application.Context.GetSystemService(Context.PowerService);
        if (pm is null) return;
        _wakeLock = pm.NewWakeLock(WakeLockFlags.Partial, "MediaOrganizer:transfer");
        _wakeLock.Acquire();
    }

    public void Release()
    {
        _wakeLock?.Release();
        _wakeLock = null;
    }
}
