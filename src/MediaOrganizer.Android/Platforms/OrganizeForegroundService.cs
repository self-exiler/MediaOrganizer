using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 归档任务前台服务（dataSync 类型）：常驻通知展示进度并提供「停止」操作，
/// 把进程优先级抬到前台——退后台/锁屏后传输继续、网络不受 Doze 限制（评估文档 M1）。
/// Android 15+ 对 dataSync 有 24 小时累计约 6 小时的时长配额，触顶回调 OnTimeout，
/// 必须数秒内停止自身，否则系统抛 RemoteServiceException。
/// </summary>
/// <remarks>
/// 本文件命名空间为 MediaOrganizer.Android.Platforms，标识符 Android 会先命中
/// MediaOrganizer.Android 命名空间（命名空间遮蔽），故所有 Android SDK 类型必须 global:: 全限定。
/// 另注：Mono.Android 中该「前台服务类型」是枚举 Android.Content.PM.ForegroundService
/// （成员名 TypeDataSync / TypeCamera…），不存在 ForegroundServiceType 这个类型名。
/// </remarks>
[Service(Name = "org.mediaorganizer.android.organize",
    Exported = false,
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class OrganizeForegroundService : Service
{
    public const string ActionCancel = "org.mediaorganizer.android.action.CANCEL_ORGANIZE";
    private const int NotificationId = 0x4d4f;
    private const int SummaryNotificationId = 0x4d50;

    private readonly Handler _mainHandler = new(Looper.MainLooper!);
    private bool _subscribed;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var host = OrganizeJobHost.Current;

        if (intent?.Action == ActionCancel)
        {
            host?.Cancel();
            FinalizeAndStop("已停止，已完成文件保留");
            return StartCommandResult.NotSticky;
        }

        if (host is null || !host.IsJobRunning)
        {
            // 无运行中任务（任务已结束/服务被系统误拉起）：不占前台位，直接退出
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        // 三参 StartForeground：API 29+ 必须显式声明与 manifest 一致的 foregroundServiceType
        StartForeground(NotificationId, BuildNotification(host), global::Android.Content.PM.ForegroundService.TypeDataSync);

        if (!_subscribed)
        {
            _subscribed = true;
            host.StateChanged += OnHostStateChanged;
            host.Finished += OnHostFinished;
            // 订阅前任务可能刚好结束（竞态）：复查一次，避免常驻通知滞留
            if (!host.IsJobRunning)
                FinalizeAndStop("任务已结束");
        }
        return StartCommandResult.NotSticky;
    }

    private void OnHostStateChanged()
    {
        var host = OrganizeJobHost.Current;
        if (host is null || !host.IsJobRunning) return;
        var notification = BuildNotification(host);
        _mainHandler.Post(() => NotifyOrIgnore(NotificationId, notification));
    }

    private void OnHostFinished(string outcome) => FinalizeAndStop(outcome);

    private void FinalizeAndStop(string outcome)
    {
        Unsubscribe();
        _mainHandler.Post(() =>
        {
            // 先撤前台态，再发非常驻的最终摘要通知（独立 ID，不与进行中通知冲突）
            StopForeground(StopForegroundFlags.Remove);
            // 链式调用逐个返回值在 AndroidX 绑定里都标了可空，链式 dereference 会刷 CS8602 告警；
            // Builder 方法本身返回 this（标准 Builder 模式），故拆成语句调用语义等价。
            var builder = new NotificationCompat.Builder(this, NotificationChannels.OrganizeChannelId)!;
            builder.SetSmallIcon(Resource.Mipmap.ic_launcher);
            builder.SetContentTitle("MediaOrganizer");
            builder.SetContentText(outcome);
            builder.SetContentIntent(BuildContentIntent());
            builder.SetAutoCancel(true);
            NotifyOrIgnore(SummaryNotificationId, builder.Build()!);
            StopSelf();
        });
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        var host = OrganizeJobHost.Current;
        if (host is null) return;
        host.StateChanged -= OnHostStateChanged;
        host.Finished -= OnHostFinished;
    }

    private Notification BuildNotification(OrganizeJobHost host)
    {
        var (title, detail, fraction) = host.Snapshot();
        var builder = new NotificationCompat.Builder(this, NotificationChannels.OrganizeChannelId)!;
        builder.SetSmallIcon(Resource.Mipmap.ic_launcher);
        builder.SetContentTitle(title);
        builder.SetContentText(detail);
        builder.SetContentIntent(BuildContentIntent());
        builder.SetOnlyAlertOnce(true);
        builder.SetOngoing(true);
        builder.AddAction(0, "停止", BuildCancelIntent());
        if (fraction is { } f && f >= 0)
            builder.SetProgress(100, Math.Clamp((int)(f * 100), 0, 100), false);
        else
            builder.SetProgress(0, 0, true); // 不定进度
        return builder.Build()!;
    }

    // GetActivity/GetService 的返回值在绑定里标了可空（targeting API 31+ 时必须带 immutable flag，
    // 取不到才为 null；此处调用参数恒定合法），加 ! 表明契约非空。
    private PendingIntent BuildContentIntent()
        => PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

    private PendingIntent BuildCancelIntent()
        => PendingIntent.GetService(this, 1,
            new Intent(this, typeof(OrganizeForegroundService)).SetAction(ActionCancel),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

    private void NotifyOrIgnore(int id, Notification notification)
    {
        var manager = NotificationManagerCompat.From(this);
        if (manager is null) return; // 理论上不会；取不到就静默丢弃，不影响任务本身
        try
        {
            manager.Notify(id, notification);
        }
        catch (Exception ex)
        {
            // POST_NOTIFICATIONS 未授权等场景：通知静默丢弃，App 内进度仍可见
            System.Diagnostics.Debug.WriteLine($"[OrganizeForegroundService] 通知发送失败：{ex.Message}");
        }
    }

    // Android 15+（API 35）dataSync 时长配额触顶：数秒内必须停止自身。
    // 任务无落盘持久化（M2 未做），此处优雅取消 + 告知原因；重跑靠 skip 策略不会重传已完成文件。
    public override void OnTimeout(int startId, global::Android.Content.PM.ForegroundService fgsType)
        => OnTimeout(startId);

    public override void OnTimeout(int startId)
    {
        OrganizeJobHost.Current?.CancelBySystemTimeout();
        FinalizeAndStop("已达系统后台时长上限，任务已停止；重新执行会跳过已完成文件");
    }
}
