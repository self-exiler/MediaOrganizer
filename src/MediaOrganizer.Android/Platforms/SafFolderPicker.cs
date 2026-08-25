using Android.Content;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 目录选取：先确保全局存储权限已授予，再经 ACTION_OPEN_DOCUMENT_TREE 选取，
/// 树 URI 确定性转换为真实路径返回（扫描/写入走 System.IO 快路径）；
/// 不可映射的提供方（网盘/第三方文档提供方）回退返回 content: 树 URI 字符串。
/// </summary>
public sealed class SafFolderPicker(MainActivity activity) : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
        if (!await AndroidStorageAccess.EnsureAsync(activity)) return null;

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

        // 权限可能刚被用户关闭（在设置页顺手关掉）：复查，避免拿到不可读目录
        if (!AndroidStorageAccess.HasAllFilesAccess) return null;

        return SafPaths.TryTreeUriToPath(uri.ToString()!) ?? uri.ToString();
    }
}
