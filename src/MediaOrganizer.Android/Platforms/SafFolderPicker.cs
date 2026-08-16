using Android.App;
using Android.Content;
using Android.OS;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// SAF 目录选取（FR-A1.1/FR-A1.5）：ACTION_OPEN_DOCUMENT_TREE + takePersistableUriPermission
/// 持久化授权（应用重启后无需重新选取）。
/// </summary>
public sealed class SafFolderPicker : IFolderPicker
{
    private readonly MainActivity _activity;

    public SafFolderPicker(MainActivity activity)
    {
        _activity = activity;
    }

    public async Task<string?> PickFolderAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission
            | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);

        var data = await _activity.StartForResultAsync(intent, RequestCodes.FolderPick);
        var uri = data?.Data;
        if (uri is null) return null;

        var flags = ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission
            | ActivityFlags.GrantPersistableUriPermission;
        try
        {
            _activity.ContentResolver.TakePersistableUriPermission(uri, flags);
        }
        catch (Java.Lang.SecurityException)
        {
            // 部分提供者不支持持久化授权，忽略
        }
        return uri.ToString();
    }
}