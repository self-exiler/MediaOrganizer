using Android.App;
using Android.Content;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 文件导出（FR-A4.3 报告「另存为/分享」）：另存为走 SAF ACTION_CREATE_DOCUMENT；
/// 分享走系统分享面板（ACTION_SEND text/plain）。
/// </summary>
public sealed class AndroidFileSaver : IFileSaver
{
    private readonly MainActivity _activity;

    public AndroidFileSaver(MainActivity activity)
    {
        _activity = activity;
    }

    public async Task<bool> SaveTextAsync(string suggestedName, string content)
    {
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("text/plain");
        intent.PutExtra(Intent.ExtraTitle, suggestedName);

        var data = await _activity.StartForResultAsync(intent, RequestCodes.CreateDocument);
        var uri = data?.Data;
        if (uri is null) return false;

        using var stream = _activity.ContentResolverInstance.OpenOutputStream(uri);
        using var writer = new System.IO.StreamWriter(stream);
        await writer.WriteAsync(content);
        await writer.FlushAsync();
        return true;
    }

    public Task ShareTextAsync(string title, string content)
    {
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(Intent.ExtraSubject, title);
        intent.PutExtra(Intent.ExtraText, content);
        _activity.StartActivity(Intent.CreateChooser(intent, title));
        return Task.CompletedTask;
    }
}
