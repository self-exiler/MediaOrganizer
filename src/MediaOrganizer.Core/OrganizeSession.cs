using MediaOrganizer.Core.Analysis;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Execution;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Core.Planning;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core;

/// <summary>一次整理会话：持有分析结果与计划，级别/操作变更即重规划，执行时快照配置（SRS FR-4/FR-5 编排）。</summary>
/// <remarks>
/// 把"分析 → 计划 → 执行"的编排从 GUI 层收进 Core：任何影响目标路径的选项变更都会立即重建计划，
/// 避免 GUI 层"改分级后执行旧计划"的过期计划 bug。
/// </remarks>
public sealed class OrganizeSession
{
    private readonly AppConfig _config;
    private AnalysisResult? _result;
    private string _outputRoot = "";
    private ClassificationLevel _level;
    private FileOperation _operation;
    private ExistAction _existAction;
    private bool _fixMtime;

    /// <summary>计划重建后触发（级别/操作/同名策略变更或新结果就位）。</summary>
    public event Action<ArchivePlan>? PlanChanged;

    public OrganizeSession(
        AppConfig config,
        ClassificationLevel level = ClassificationLevel.Day,
        FileOperation operation = FileOperation.Copy,
        ExistAction existAction = ExistAction.Skip,
        bool fixMtime = false)
    {
        _config = config;
        _level = level;
        _operation = operation;
        _existAction = existAction;
        _fixMtime = fixMtime;
    }

    public ArchivePlan? CurrentPlan { get; private set; }

    public bool HasResult => _result is not null;

    /// <summary>最近一次分析结果（GUI 变更输出目标展示时重设计划用）。</summary>
    public AnalysisResult? LastResult => _result;

    public ClassificationLevel Level
    {
        get => _level;
        set { if (_level != value) { _level = value; RebuildPlan(); } }
    }

    public FileOperation Operation
    {
        get => _operation;
        set { if (_operation != value) { _operation = value; NotifyPlanChanged(); } }
    }

    public ExistAction ExistAction
    {
        get => _existAction;
        set { if (_existAction != value) { _existAction = value; NotifyPlanChanged(); } }
    }

    public bool FixMtime
    {
        get => _fixMtime;
        set { if (_fixMtime != value) { _fixMtime = value; NotifyPlanChanged(); } }
    }

    public void SetResult(AnalysisResult result, string outputRoot)
    {
        _result = result;
        _outputRoot = outputRoot;
        RebuildPlan();
    }

    /// <summary>规则/配置变更后使既有计划失效（等待重新分析）。</summary>
    public void InvalidateResult()
    {
        _result = null;
        CurrentPlan = null;
    }

    private void RebuildPlan()
    {
        if (_result is null) return;
        CurrentPlan = new ArchivePlanner(_level).Plan(_result, _outputRoot);
        PlanChanged?.Invoke(CurrentPlan);
    }

    private void NotifyPlanChanged()
    {
        if (CurrentPlan is not null) PlanChanged?.Invoke(CurrentPlan);
    }

    /// <summary>执行当前计划；配置在调用瞬间快照，避免执行中途被 UI 改动。</summary>
    public async Task<FileOperationResult> ExecuteAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (CurrentPlan is null)
            throw new InvalidOperationException("尚无计划，请先分析");

        var plan = CurrentPlan;
        var storage = StorageFactory.CreateTarget(_config);
        var isNetwork = !string.IsNullOrEmpty(_config.Paths.OutputNetworkProfile);
        var parallelism = _config.Execute.MaxDegreeOfParallelism > 0
            ? _config.Execute.MaxDegreeOfParallelism
            : (isNetwork ? 4 : 2);
        var executor = new FileOperator(storage, _operation, _existAction, _fixMtime, parallelism);
        return await Task.Run(() => executor.ExecuteAsync(plan, progress, ct), ct);
    }
}
