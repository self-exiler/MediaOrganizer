using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Execution;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Planning;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core;

/// <summary>一次整理会话：分析 → 规划 → 执行的完整流程 Facade（SRS FR-4/FR-5 编排）。</summary>
/// <remarks>
/// 把"分析 → 计划 → 执行"的完整编排从 GUI 层收进 Core：Analyzer 的创建和调用、结果落盘、
/// 计划重建、执行快照配置——全部在此协调。ViewModel 只需调 AnalyzeAsync / ExecuteAsync 两个入口。
/// 任何影响目标路径的选项变更都会立即重建计划，避免 GUI 层"改分级后执行旧计划"的过期计划 bug。
/// 线程模型：结果/计划/选项由分析后台线程与 UI 线程交叉读写，全部可变状态统一过 _sync；
/// 事件一律在锁外触发，避免回调重入死锁。
/// </remarks>
public sealed class OrganizeSession
{
    private readonly AppConfig _config;
    private readonly Func<Analyzer> _analyzerFactory;
    private readonly string _dataDir;
    private readonly object _sync = new();
    private AnalysisResult? _result;
    private string _outputRoot = "";
    private ClassificationLevel _level;
    private FileOperation _operation;
    private ExistAction _existAction;
    private bool _fixMtime;
    private ArchivePlan? _currentPlan;

    /// <summary>计划重建后触发（级别/操作/同名策略变更或新结果就位）。</summary>
    public event Action<ArchivePlan>? PlanChanged;

    /// <summary>分析完成时触发（结果已落盘，计划已就绪），供 ViewModel 通知下游刷新。</summary>
    public event Action<AnalysisResult>? AnalysisCompleted;

    public OrganizeSession(
        AppConfig config,
        Func<Analyzer> analyzerFactory,
        string dataDir,
        ClassificationLevel level = ClassificationLevel.Day,
        FileOperation operation = FileOperation.Copy,
        ExistAction existAction = ExistAction.Skip,
        bool fixMtime = false)
    {
        _config = config;
        _analyzerFactory = analyzerFactory;
        _dataDir = dataDir;
        _level = level;
        _operation = operation;
        _existAction = existAction;
        _fixMtime = fixMtime;
    }

    public ArchivePlan? CurrentPlan
    {
        get { lock (_sync) return _currentPlan; }
        private set { lock (_sync) _currentPlan = value; }
    }

    /// <summary>最近一次分析生成的报告文本（Session 落盘时已生成一份，报告页直接复用，避免在 UI 线程重复做全量分组统计）。</summary>
    public string? LastReportText { get; private set; }

    public bool HasResult { get { lock (_sync) return _result is not null; } }

    /// <summary>最近一次分析结果（GUI 变更输出目标展示时重设计划用）。</summary>
    public AnalysisResult? LastResult { get { lock (_sync) return _result; } }

    public ClassificationLevel Level
    {
        get { lock (_sync) return _level; }
        set
        {
            bool changed;
            lock (_sync) { changed = _level != value; if (changed) _level = value; }
            if (changed) RebuildPlan();
        }
    }

    public FileOperation Operation
    {
        get { lock (_sync) return _operation; }
        set
        {
            bool changed;
            lock (_sync) { changed = _operation != value; if (changed) _operation = value; }
            if (changed) NotifyPlanChanged();
        }
    }

    public ExistAction ExistAction
    {
        get { lock (_sync) return _existAction; }
        set
        {
            bool changed;
            lock (_sync) { changed = _existAction != value; if (changed) _existAction = value; }
            if (changed) NotifyPlanChanged();
        }
    }

    public bool FixMtime
    {
        get { lock (_sync) return _fixMtime; }
        set
        {
            bool changed;
            lock (_sync) { changed = _fixMtime != value; if (changed) _fixMtime = value; }
            if (changed) NotifyPlanChanged();
        }
    }

    /// <summary>分析源目录：创建 Analyzer → 并行提取 → TXT 报告落盘 → 重建计划。</summary>
    public async Task<AnalysisResult> AnalyzeAsync(
        string sourceDir,
        string outputRoot,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        var analyzer = _analyzerFactory();
        var result = await analyzer.AnalyzeAsync(sourceDir, progress, ct);

        // TXT 报告落盘到应用专有目录，不写源目录；
        // analysis-result.json 已废弃：内存结果直达魔术工具，磁盘只留人读的 TXT 报告。
        // 文件写是同步 IO，且续体可能回到 UI 线程 → 移出主线程，避免阻塞界面。
        string reportText = "";
        await Task.Run(() =>
        {
            try
            {
                reportText = AnalysisReportGenerator.Generate(result, outputRoot);
                File.WriteAllText(Path.Combine(_dataDir, "analysis-report.txt"), reportText);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrganizeSession] 报告落盘失败: {ex.Message}");
            }
        }, ct);
        // 供报告页取用（避免 ReportViewModel.Set 再跑一遍同样的全量分组统计）
        LastReportText = reportText;

        SetResult(result, outputRoot);
        AnalysisCompleted?.Invoke(result);
        return result;
    }

    public void SetResult(AnalysisResult result, string outputRoot)
    {
        lock (_sync)
        {
            _result = result;
            _outputRoot = outputRoot;
        }
        RebuildPlan();
    }

    /// <summary>规则/配置变更后使既有计划失效（等待重新分析）。</summary>
    public void InvalidateResult()
    {
        lock (_sync)
        {
            _result = null;
            _currentPlan = null;
        }
    }

    private void RebuildPlan()
    {
        ArchivePlan? plan;
        ClassificationLevel level;
        int bufferDays;
        lock (_sync)
        {
            if (_result is null) return;
            level = _level;
            bufferDays = _config.Extraction.FutureDateBufferDays;
            // 未来日期缓冲天数必须与 DateRangeValidator 同源：校验器已按配置放行明天的文件，
            // 规划器若按 buffer=0 归档到 FutureDate/ 则口径不一致。
            _currentPlan = new ArchivePlanner(level, bufferDays).Plan(_result, _outputRoot);
            plan = _currentPlan;
        }
        PlanChanged?.Invoke(plan);
    }

    private void NotifyPlanChanged()
    {
        var plan = CurrentPlan;
        if (plan is not null) PlanChanged?.Invoke(plan);
    }

    /// <summary>执行当前计划；配置在调用瞬间统一快照，避免执行中途被 UI 改动（与状态锁同口径）。</summary>
    public async Task<FileOperationResult> ExecuteAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArchivePlan? plan;
        FileOperation operation;
        ExistAction existAction;
        bool fixMtime;
        lock (_sync)
        {
            if (_result is null || _currentPlan is null)
                throw new InvalidOperationException("尚无计划，请先分析");
            plan = _currentPlan;
            operation = _operation;
            existAction = _existAction;
            fixMtime = _fixMtime;
        }

        // SMB 存储持有 TCP 连接 + 会话，必须释放，否则每次归档泄漏一条连接
        using var storage = StorageFactory.CreateTarget(_config);
        var isNetwork = !string.IsNullOrEmpty(_config.Paths.OutputNetworkProfile);
        var parallelism = _config.Execute.MaxDegreeOfParallelism > 0
            ? _config.Execute.MaxDegreeOfParallelism
            : (isNetwork ? 4 : 2);
        var executor = new FileOperator(storage, operation, existAction, fixMtime, parallelism);
        // 不把 ct 传给 Task.Run：已取消时它会在调用线程同步抛 OCE，绕过上层对任务异常的捕获；
        // 取消由 Parallel.ForEachAsync 的 CancellationToken（ParallelOptions）统一处理。
        return await Task.Run(() => executor.ExecuteAsync(plan, progress, ct));
    }
}
