using Android.App;
using Android.Content;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 用系统默认应用打开失败文件（FR-A6.3）：ACTION_VIEW Intent + FLAG_GRANT_READ_URI_PERMISSION。
/// identifier 为 content:// 文档 URI。
/// </summary>
public sealed class AndroidSystemFileOpener : ISystemFileOpener
{
    private readonly MainActivity _activity;

    public AndroidSystemFileOpener(MainActivity activity)
    {
        _activity = activity;
    }

    public Task OpenAsync(string identifier)
    {
        var uri = global::Android.Net.Uri.Parse(identifier);
        if (uri is null) return Task.CompletedTask;

        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "*/*");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        try
        {
            _activity.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            // 无可用应用打开该类型，忽略
        }
        return Task.CompletedTask;
    }
}
