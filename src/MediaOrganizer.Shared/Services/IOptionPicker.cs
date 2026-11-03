namespace MediaOrganizer.Shared.Services;

/// <summary>
/// 单选对话框抽象（与 IConfirmDialog 并列）：触屏选择字段（OptionPicker）点击后弹出，
/// 取代桌面导向的 ComboBox 弹层（审查报告2 §2.3 方案A / 报告3 方案A）。
/// Android 实现为系统 AlertDialog 单选列表；桌面暂无消费方。
/// </summary>
public interface IOptionPicker
{
    /// <summary>
    /// 弹出单选对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="options">选项文本列表。</param>
    /// <param name="selectedIndex">当前选中索引，无选中传 -1。</param>
    /// <returns>用户选中的索引；取消 / 返回键 / 点外部关闭返回 -1。</returns>
    Task<int> PickAsync(string title, IReadOnlyList<string> options, int selectedIndex);
}
