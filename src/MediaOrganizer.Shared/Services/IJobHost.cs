using MediaOrganizer.Core;
using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Execution;

namespace MediaOrganizer.Shared.Services;

/// <summary>
/// 执行宿主抽象：把「分析/执行任务」从 ViewModel 内存提升到进程级载体
/// （Android = OrganizeJobHost + dataSync 前台服务，退后台/锁屏不被回收；取消令牌随宿主，Activity 重建不失效）。
/// 桌面端注入 null → VM 走内置的 await 路径，行为与历史版本一致。
/// 约定：Start* 必须在 UI 线程调用（宿主内 new Progress&lt;T&gt; 与 session 事件的线程语义依赖 Avalonia 同步上下文）；
/// onCompleted / onFailed / onCanceled 由宿主保证 marshal 回 UI 线程后调用。
/// </summary>
public interface IJobHost
{
    /// <summary>当前是否有托管任务在运行（分析或执行任一）。</summary>
    bool IsJobRunning { get; }

    void StartAnalysis(
        OrganizeSession session,
        string sourceDir,
        string outputRoot,
        IProgress<AnalysisProgress> progress,
        Action onCompleted,
        Action<Exception> onFailed,
        Action onCanceled);

    void StartExecution(
        OrganizeSession session,
        IProgress<double> progress,
        Action<FileOperationResult> onCompleted,
        Action<Exception> onFailed,
        Action onCanceled);

    /// <summary>取消当前托管任务（语义与 VM 内置路径的 _cts.Cancel 一致：优雅停止，已完成文件保留）。</summary>
    void Cancel();
}
