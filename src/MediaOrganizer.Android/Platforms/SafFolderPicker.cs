using Android.Content;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 目录选取：先确保全局存储权限已授予，再经 ACTION_OPEN_DOCUMENT_TREE 选取，
/// 树 URI 确定性转换为真实路径返回（扫描/写入走 System.IO 快路径）；
/// 不可映射的提供方返回 null（下游对 content: 标识符一律报错，返回它必然失败）。
/// </summary>
public sealed class SafFolderPicker(MainActivity activity) : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
        if (!await AndroidStorageAccess.EnsureAsync(activity)) return null;

        // 不再申请持久化 URI 权限：全仓库无 GetPersistedUriPermissions 读取方，扫描/写入均走真实路径（走查 #17）
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        var data = await activity.StartForResultAsync(intent, RequestCodes.FolderPick);
        var uri = data?.Data;
        if (uri is null) return null;

        // 权限可能刚被用户关闭（在设置页顺手关掉）：复查，避免拿到不可读目录
        if (!AndroidStorageAccess.HasAllFilesAccess) return null;

        return SafPaths.TryTreeUriToPath(uri.ToString()!);
    }
}
