using Android.Content;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 文件导出 / 分享（FR-A 报告页）：另存为走 ACTION_CREATE_DOCUMENT（SAF）；
/// 分享走系统分享面板（ACTION_SEND 纯文本）。
/// </summary>
public sealed class AndroidFileSaver(MainActivity activity) : IFileSaver
{
    public async Task<bool> SaveTextAsync(string suggestedName, string content)
    {
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.SetType("text/plain");
        intent.AddCategory(Intent.CategoryOpenable);
        intent.PutExtra(Intent.ExtraTitle, suggestedName);

        var data = await activity.StartForResultAsync(intent, RequestCodes.CreateDocument);
        var uri = data?.Data;
        if (uri is null) return false;

        await using var stream = activity.Resolver.OpenOutputStream(uri)
                                  ?? throw new IOException("无法打开输出流");
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
        return true;
    }

    public Task ShareTextAsync(string title, string content)
    {
        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraSubject, title);
        send.PutExtra(Intent.ExtraText, content);
        activity.StartActivity(Intent.CreateChooser(send, title));
        return Task.CompletedTask;
    }
}
