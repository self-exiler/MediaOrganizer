using Android.Content;
using Android.OS;
using Android.Provider;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 全局存储权限（MANAGE_EXTERNAL_STORAGE，"所有文件访问"）：
/// API 30+ 需经系统设置页授权（无法弹窗直接授予），授权后可直读真实路径，
/// 扫描/整理走 System.IO（远快于 SAF DocumentFile 逐文件 IPC）。API 30 以下声明即可用。
/// </summary>
public static class AndroidStorageAccess
{
    public const int RequestCode = 0x1003;

    /// <summary>当前是否已拥有所有文件访问权限。</summary>
    public static bool HasAllFilesAccess
        => !OperatingSystem.IsAndroid()
           || Build.VERSION.SdkInt < BuildVersionCodes.R
           || global::Android.OS.Environment.IsExternalStorageManager;

    /// <summary>
    /// 确保已授权：未授权时跳转系统设置页，等待用户返回后复查（该页返回 RESULT_OK 不可靠，以运行时状态为准）。
    /// SupportedOSPlatformVersion=33，ActionManageAppAllFilesAccessPermission 必然存在，无需回退分支。
    /// </summary>
    public static async Task<bool> EnsureAsync(MainActivity activity)
    {
        if (HasAllFilesAccess) return true;

        var intent = new Intent(
            Settings.ActionManageAppAllFilesAccessPermission,
            global::Android.Net.Uri.Parse("package:" + activity.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);

        try
        {
            await activity.StartForResultAsync(intent, RequestCode);
        }
        catch (TimeoutException)
        {
            // 用户在设置页停留超过 StartForResultAsync 的 60s 超时：不视为失败
        }

        return HasAllFilesAccess;
    }
}

/// <summary>
/// SAF 树 URI ↔ 真实路径映射：externalstorage 文档提供方的 documentId 形如
/// "primary:DCIM" / "XXXX-XXXX:sub"，确定性映射到 /storage/…；其他提供方不可映射。
/// 约定：以 content: 开头的字符串为 SAF 标识，其余为绝对路径。
/// </summary>
public static class SafPaths
{
    public static bool IsSafIdentifier(string? path)
        => !string.IsNullOrEmpty(path) && path.StartsWith("content:", StringComparison.OrdinalIgnoreCase);

    /// <summary>把 ACTION_OPEN_DOCUMENT_TREE 的树 URI 转换为真实目录路径；不可映射返回 null。</summary>
    public static string? TryTreeUriToPath(string treeUriString)
    {
        var uri = global::Android.Net.Uri.Parse(treeUriString);
        var docId = uri is null ? null : DocumentsContract.GetTreeDocumentId(uri);
        return TryDocumentIdToPath(docId);
    }

    /// <summary>"primary:DCIM" → "/storage/emulated/0/DCIM"；"XXXX-XXXX:a/b" → "/storage/XXXX-XXXX/a/b"。</summary>
    internal static string? TryDocumentIdToPath(string? documentId)
    {
        if (string.IsNullOrEmpty(documentId)) return null;
        var sep = documentId.IndexOf(':');
        if (sep <= 0 || sep == documentId.Length - 1) return null;
        var root = documentId[..sep];
        var relative = documentId[(sep + 1)..].Replace('/', Path.DirectorySeparatorChar);
        return root switch
        {
            "primary" => Path.Combine("/storage/emulated/0", relative),
            _ => IsVolumeId(root) ? Path.Combine("/storage", root, relative) : null,
        };
    }

    // 卷 ID 形如十六进制 UUID 前段（SD 卡 / U 盘，"1234-5678"），排除 "home"、"downloads" 等非存储卷
    private static bool IsVolumeId(string root)
        => root.Length == 9 && root.All(c => char.IsAsciiHexDigit(c) || c == '-');
}
