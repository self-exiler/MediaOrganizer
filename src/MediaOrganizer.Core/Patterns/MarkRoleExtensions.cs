namespace MediaOrganizer.Core.Patterns;

/// <summary>MarkRole 扩展方法：角色标签文本统一定义，消除 MagicToolsViewModel 和 MarkableCharVM 中的重复。</summary>
public static class MarkRoleExtensions
{
    public static string Label(this MarkRole role) => role switch
    {
        MarkRole.Year => "年",
        MarkRole.Month => "月",
        MarkRole.Day => "日",
        MarkRole.Hour => "时",
        MarkRole.Minute => "分",
        MarkRole.Second => "秒",
        MarkRole.Timestamp => "时间戳",
        MarkRole.Ignore => "忽略",
        MarkRole.Required => "必现",
        _ => "未标记"
    };
}
