using Android.Content;
using Android.Webkit;
using AndroidX.Core.Content;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 用系统默认应用打开文件（FR-A6.3）：ACTION_VIEW + FLAG_GRANT_READ_URI_PERMISSION。
/// content: 标识直接用文档 URI（SAF 授权，MIME 经 ContentResolver 解析）；
/// 真实路径经 FileProvider 生成可共享 URI（Android 7+ 禁止跨应用传 file://）。
/// 无接收 App 时静默失败（有 Toast 提示）。
/// </summary>
public sealed class AndroidSystemFileOpener(MainActivity activity) : ISystemFileOpener
{
    public Task OpenAsync(string identifier)
    {
        try
        {
            var isSaf = SafPaths.IsSafIdentifier(identifier);
            var uri = isSaf
                ? global::Android.Net.Uri.Parse(identifier)
                : ToFileProviderUri(identifier);
            if (uri is null) return Task.CompletedTask;

            var mime = isSaf
                ? activity.Resolver.GetType(uri) ?? "application/octet-stream"
                : GetMimeType(identifier);

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, mime);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            // 直接走系统解析（不受 软件包可见性 过滤约束），由 catch 处理「确无接收 App」的 ActivityNotFoundException
            activity.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            ShowToast(activity, "没有应用能打开此文件类型");
        }
        catch
        {
            ShowToast(activity, "打开失败");
        }
        return Task.CompletedTask;
    }

    /// <summary>真实路径 → FileProvider URI；文件不存在时提示并返回 null。</summary>
    private global::Android.Net.Uri? ToFileProviderUri(string path)
    {
        if (!File.Exists(path))
        {
            ShowToast(activity, "文件不存在或已被移动");
            return null;
        }
        return FileProvider.GetUriForFile(
            activity, $"{activity.PackageName}.fileprovider", new Java.IO.File(path));
    }

    private static string GetMimeType(string path)
        => MimeTypeMap.Singleton?.GetMimeTypeFromExtension(
               System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant())
           ?? "application/octet-stream";

    private static void ShowToast(MainActivity activity, string message)
        => activity.RunOnUiThread(() =>
            global::Android.Widget.Toast.MakeText(activity, message, global::Android.Widget.ToastLength.Short)?.Show());
}
