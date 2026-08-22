using Android.Content;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 用系统默认应用打开文件（FR-A6.3）：ACTION_VIEW + FLAG_GRANT_READ_URI_PERMISSION，
/// identifier 为 SAF 文档 URI；无接收 App 时静默失败（有 Toast 提示）。
/// </summary>
public sealed class AndroidSystemFileOpener(MainActivity activity) : ISystemFileOpener
{
    public Task OpenAsync(string identifier)
    {
        try
        {
            var uri = global::Android.Net.Uri.Parse(identifier);
            if (uri is null) return Task.CompletedTask;

            var intent = new Intent(Intent.ActionView, uri);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            intent.AddFlags(ActivityFlags.NewTask);
            if (intent.ResolveActivity(activity.PackageManager) is null)
            {
                ShowToast(activity, "没有应用能打开此文件类型");
                return Task.CompletedTask;
            }
            activity.StartActivity(intent);
        }
        catch
        {
            ShowToast(activity, "打开失败");
        }
        return Task.CompletedTask;
    }

    private static void ShowToast(MainActivity activity, string message)
        => activity.RunOnUiThread(() =>
            global::Android.Widget.Toast.MakeText(activity, message, global::Android.Widget.ToastLength.Short)?.Show());
}
