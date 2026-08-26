/// 一次整理会话：分析 → 规划 → 执行的完整流程 Facade
/// （对应 Core/OrganizeSession.cs）。ViewModel 只需调 analyze / execute 两个入口；
/// 任何影响目标路径的选项变更都会立即重建计划，避免"改分级后执行旧计划"。
library;

import 'dart:io';

import 'analysis_result_store.dart';
import 'analyzer.dart';
import 'app_config.dart';
import 'archive_planner.dart';
import 'enums.dart';
import 'file_operator.dart';
import '../services/storage_factory.dart';
import 'models.dart';
import 'patterns_store.dart';
import 'report_generator.dart';

class OrganizeSession {
  final AppConfig config;
  final List<PatternDefinition> patterns;
  final String dataDir;

  AnalysisResult? _result;
  ArchivePlan? _currentPlan;
  String _outputRoot = '';
  ClassificationLevel _level;
  FileOperation _operation;
  ExistAction _existAction;
  bool _fixMtime;

  /// 计划重建后触发（级别/操作/同名策略变更或新结果就位）。
  void Function(ArchivePlan plan)? onPlanChanged;

  /// 分析完成时触发（结果已落盘，计划已就绪）。
  void Function(AnalysisResult result)? onAnalysisCompleted;

  OrganizeSession({
    required this.config,
    required this.patterns,
    required this.dataDir,
    ClassificationLevel level = ClassificationLevel.day,
    FileOperation operation = FileOperation.copy,
    ExistAction existAction = ExistAction.skip,
    bool fixMtime = false,
  })  : _level = level,
        _operation = operation,
        _existAction = existAction,
        _fixMtime = fixMtime;

  ArchivePlan? get currentPlan => _currentPlan;
  bool get hasResult => _result != null;
  AnalysisResult? get lastResult => _result;

  ClassificationLevel get level => _level;
  set level(ClassificationLevel v) {
    if (_level != v) {
      _level = v;
      rebuildPlan();
    }
  }

  FileOperation get operation => _operation;
  set operation(FileOperation v) {
    if (_operation != v) {
      _operation = v;
      notifyPlanChanged();
    }
  }

  ExistAction get existAction => _existAction;
  set existAction(ExistAction v) {
    if (_existAction != v) {
      _existAction = v;
      notifyPlanChanged();
    }
  }

  bool get fixMtime => _fixMtime;
  set fixMtime(bool v) {
    if (_fixMtime != v) {
      _fixMtime = v;
      notifyPlanChanged();
    }
  }

  /// 分析源目录：启动分析 Isolate → 落盘 → 重建计划。
  Future<AnalysisResult> analyze(
    String sourceDir,
    String outputRoot, {
    void Function(AnalysisProgress progress)? onProgress,
    CancelToken? cancelToken,
  }) async {
    final run = Analyzer.run(
      sourceDir: sourceDir,
      scan: config.scan,
      extraction: config.extraction,
      patterns: patterns,
      onProgress: onProgress,
    );
    cancelToken?.onCancel(run.cancel);
    if (cancelToken?.isCancelled ?? false) run.cancel();
    final result = await run.result;

    // 落盘到应用专有目录（ADR-0006 决策 6）：analysis-result.json + TXT 报告，不写源目录
    try {
      AnalysisResultStore.save('$dataDir/analysis-result.json', result);
      final report = AnalysisReportGenerator.generate(result, outputRoot);
      File('$dataDir/analysis-report.txt').writeAsStringSync(report);
    } catch (_) {
      // 落盘失败不影响本次分析的展示与执行
    }

    setResult(result, outputRoot);
    onAnalysisCompleted?.call(result);
    return result;
  }

  void setResult(AnalysisResult result, String outputRoot) {
    _result = result;
    _outputRoot = outputRoot;
    rebuildPlan();
  }

  /// 规则/配置变更后使既有计划失效（等待重新分析）。
  void invalidateResult() {
    _result = null;
    _currentPlan = null;
  }

  void rebuildPlan() {
    final result = _result;
    if (result == null) return;
    _currentPlan = ArchivePlanner(_level).plan(result, _outputRoot);
    onPlanChanged?.call(_currentPlan!);
  }

  void notifyPlanChanged() {
    final plan = _currentPlan;
    if (plan != null) onPlanChanged?.call(plan);
  }

  /// 执行当前计划；并行度按本地/网络目标取默认或配置值。
  Future<FileOperationResult> execute({
    void Function(double progress)? onProgress,
    CancelToken? cancelToken,
  }) async {
    final plan = _currentPlan;
    if (plan == null) {
      throw StateError('尚无计划，请先分析');
    }
    final storage = StorageFactory.createTarget(config);
    final executor = FileOperator(storage, _operation, _existAction, _fixMtime,
        concurrency: config.execute.maxDegreeOfParallelism);
    return executor.execute(plan,
        onProgress: onProgress, cancelToken: cancelToken);
  }
}
