using Android.Content;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// SAF 目录选取（FR-A1.1/FR-A1.5）：ACTION_OPEN_DOCUMENT_TREE，
/// takePersistableUriPermission 持久化授权（应用重启后免重选），返回树 URI 字符串。
/// </summary>
public sealed class SafFolderPicker(MainActivity activity) : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission
                        | ActivityFlags.GrantWriteUriPermission
                        | ActivityFlags.GrantPersistableUriPermission);

        var data = await activity.StartForResultAsync(intent, RequestCodes.FolderPick);
        var uri = data?.Data;
        if (uri is null) return null;

        try
        {
            activity.Resolver.TakePersistableUriPermission(uri,
                ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
        catch
        {
            // 部分文档提供方不支持持久化：当次会话仍可用，重启后需重选
        }
        return uri.ToString();
    }
}
