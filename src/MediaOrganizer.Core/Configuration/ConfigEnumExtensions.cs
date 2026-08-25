namespace MediaOrganizer.Core.Configuration;

/// <summary>ExistAction 和 ClassificationLevel 的 UI 索引映射扩展方法，
/// 消除 WorkbenchViewModel 中 ExistIndex/ExistActionFromIndex/LevelIndexValue/LevelFromIndex 的四组重复映射。</summary>
public static class ConfigEnumExtensions
{
    // ---- ExistAction ↔ UI index ----

    public static int ToIndex(this ExistAction action) => action switch
    {
        ExistAction.Overwrite => 1,
        ExistAction.Rename => 2,
        _ => 0
    };

    public static ExistAction ToExistAction(this int index) => index switch
    {
        1 => ExistAction.Overwrite,
        2 => ExistAction.Rename,
        _ => ExistAction.Skip
    };

    // ---- ClassificationLevel ↔ UI index ----

    public static int ToIndex(this ClassificationLevel level) => level switch
    {
        ClassificationLevel.Month => 1,
        ClassificationLevel.Year => 2,
        _ => 0
    };

    public static ClassificationLevel ToClassificationLevel(this int index) => index switch
    {
        1 => ClassificationLevel.Month,
        2 => ClassificationLevel.Year,
        _ => ClassificationLevel.Day
    };
}
