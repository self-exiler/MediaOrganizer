using Android.App;

namespace MediaOrganizer.Android.Platforms;

/// <summary>通知渠道定义：Application.OnCreate 时创建（渠道重要性创建后不可改，低优先级=常驻进度不响铃）。</summary>
public static class NotificationChannels
{
    public const string OrganizeChannelId = "organize";

    public static void EnsureCreated(global::Android.Content.Context context)
    {
        var manager = (NotificationManager?)context.GetSystemService(global::Android.Content.Context.NotificationService);
        if (manager is null || manager.GetNotificationChannel(OrganizeChannelId) is not null) return;
        var channel = new NotificationChannel(OrganizeChannelId, "归档任务", NotificationImportance.Low)
        {
            Description = "后台备份/分析的进度与完成通知"
        };
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }
}
