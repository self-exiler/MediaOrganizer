using MediaOrganizer.Core.Execution;

namespace MediaOrganizer.Shared.Services;

/// <summary>
/// 执行结果统一文案：桌面状态栏与安卓「完成通知」共用同一份格式，保证双端文本一致。
/// </summary>
public static class JobText
{
    public static string DescribeExecution(FileOperationResult result)
        => $"执行完成：成功 {result.Succeeded}，跳过 {result.Skipped}，覆盖 {result.Overwritten}，重命名 {result.Renamed}，失败 {result.Failed}";
}
