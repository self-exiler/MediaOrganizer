using MediaOrganizer.Core.Execution;

namespace MediaOrganizer.Shared.Services;

/// <summary>
/// 执行结果统一文案：桌面状态栏与安卓「完成通知」共用同一份格式，保证双端文本一致。
/// </summary>
public static class JobText
{
    public static string DescribeExecution(FileOperationResult result)
    {
        var text = $"执行完成：成功 {result.Succeeded}，跳过 {result.Skipped}，覆盖 {result.Overwritten}，重命名 {result.Renamed}，失败 {result.Failed}";
        if (result.Timing is { } t && t.FileCount > 0)
            text += $"（传输均值 {t.TransferMeanMs / 1000:F1}s，P95 {t.TransferP95Ms / 1000:F1}s；元数据均值 {t.MetadataMeanMs / 1000:F1}s）";
        return text;
    }
}
