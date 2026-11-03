using Avalonia.Media;

namespace MediaOrganizer.Android;

/// <summary>
/// 矢量图标资源（Material Symbols 24×24 路径数据）：取代文本字符图标（≡ ▶ ↻ × › ⚠ ↗ ✓ ⋯）——
/// 自打包 Noto Sans SC 不保证含有这些符号字形，字体回退失败时显示方块（审查报告1 #5、报告2 F8、报告3 F5）。
/// XAML 经 x:Static 引用，控件与 VM 直接取字段；需要镂空的图形统一以 F0（EvenOdd）填充规则书写。
/// </summary>
public static class AppIcons
{
    /// <summary>≡ 汉堡菜单。</summary>
    public static readonly Geometry Menu = Geometry.Parse(
        "M3,6H21V8H3V6M3,11H21V13H3V11M3,16H21V18H3V16");

    /// <summary>▾ 选择字段下拉箭头。</summary>
    public static readonly Geometry ChevronDown = Geometry.Parse(
        "M7.41,8.59L12,13.17L16.59,8.59L18,10L12,16L6,10L7.41,8.59Z");

    /// <summary>› 右向箭头（查看失败、展开详情）。</summary>
    public static readonly Geometry ChevronRight = Geometry.Parse(
        "M8.59,16.59L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.59Z");

    /// <summary>× 取消。</summary>
    public static readonly Geometry Close = Geometry.Parse(
        "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z");

    /// <summary>▶ 开始分析 / 执行归档。</summary>
    public static readonly Geometry Play = Geometry.Parse("M8,5V19L19,12L8,5Z");

    /// <summary>↻ 重新分析。</summary>
    public static readonly Geometry Refresh = Geometry.Parse(
        "M17.65,6.35C16.2,4.9 14.21,4 12,4C7.58,4 4,7.58 4,12C4,16.42 7.58,20 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18C8.69,18 6,15.31 6,12C6,8.69 8.69,6 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z");

    /// <summary>↗ 用系统应用打开。</summary>
    public static readonly Geometry OpenInNew = Geometry.Parse(
        "M19,19H5V5H10V3H5C3.89,3 3,3.9 3,5V19C3,20.1 3.89,21 5,21H19C20.1,21 21,20.1 21,19V14H19V19Z M14,3V5H17.59L7.76,14.83L9.17,16.24L19,6.41V10H21V3H14Z");

    /// <summary>✓ 已验证。</summary>
    public static readonly Geometry Check = Geometry.Parse(
        "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z");

    /// <summary>⋯ 更多操作（网络位置行尾菜单）。</summary>
    public static readonly Geometry Dots = Geometry.Parse(
        "M4,12A2,2 0 1,0 8,12A2,2 0 1,0 4,12Z M10,12A2,2 0 1,0 14,12A2,2 0 1,0 10,12Z M16,12A2,2 0 1,0 20,12A2,2 0 1,0 16,12Z");

    /// <summary>抽屉：整理工作台（文件夹）。</summary>
    public static readonly Geometry Folder = Geometry.Parse(
        "M10,4H4C2.89,4 2,4.89 2,6V18C2,19.1 2.89,20 4,20H20C21.1,20 22,19.1 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z");

    /// <summary>抽屉：失败文件（警示三角，感叹号为镂空，EvenOdd）。</summary>
    public static readonly Geometry Alert = Geometry.Parse(
        "F0 M12,2L1,21H23L12,2Z M11,10H13V14.5H11V10Z M11,16.5H13V18.5H11V16.5Z");

    /// <summary>抽屉：分析报告（文档，文字行为镂空，EvenOdd）。</summary>
    public static readonly Geometry Document = Geometry.Parse(
        "F0 M14,2H6C4.9,2 4,2.9 4,4V20C4,21.1 4.9,22 6,22H18C19.1,22 20,21.1 20,20V8L14,2Z M8,12H16V13.5H8V12Z M8,15.5H16V17H8V15.5Z");

    /// <summary>抽屉：设置（调节滑杆）。</summary>
    public static readonly Geometry Tune = Geometry.Parse(
        "M3,5H21V7H3V5Z M11,3H13V9H11V3Z M3,11H21V13H3V11Z M6,9H8V15H6V9Z M3,17H21V19H3V19Z M15,15H17V21H15V21Z");
}
