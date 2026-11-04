namespace MediaOrganizer.Core.Storage;

/// <summary>归档计划路径（恒为 '/' 分隔）的公共拆分口径：收口 SMB/WebDAV/查重/传输四处重复实现（走查 #19）。</summary>
internal static class PlanPath
{
    /// <summary>返回父目录与叶子名；无父目录时 Dir 为空串。</summary>
    public static (string Dir, string Leaf) Split(string relativePath)
    {
        var idx = relativePath.LastIndexOf('/');
        return idx <= 0
            ? ("", relativePath.TrimStart('/'))
            : (relativePath[..idx], relativePath[(idx + 1)..]);
    }
}
