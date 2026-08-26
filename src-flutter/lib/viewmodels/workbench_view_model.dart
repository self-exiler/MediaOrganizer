/// 整理工作台 VM（对应 Shared/ViewModels/WorkbenchViewModel.cs）：
/// 选目录 → 分析 → 确认计划 → 执行归档。目录选取走 FolderPicker；
/// 分析在独立 Isolate 中执行；结果与报告落盘应用专有目录。
library;

import 'dart:async';

import 'package:flutter/foundation.dart';

import '../core/analyzer.dart' show AnalysisCancelledException;
import '../core/analysis_result_store.dart';
import '../core/app_state.dart';
import '../core/enums.dart';
import '../core/file_operator.dart';
import '../core/format.dart';
import '../core/models.dart';
import '../core/organize_session.dart';
import '../services/platform_services.dart';

class WorkbenchViewModel extends ChangeNotifier {
  final AppState state;
  final String dataDir;

  late final OrganizeSession _session;
  CancelToken? _cancelToken;

  /// 输出目标下拉选中网络位置时跳转设置页（原型 "+ 添加网络位置…"）。
  void Function()? onNavigateToSettings;

  /// 分析结果卡片「查看 N 个失败」跳转失败文件页。
  void Function()? onNavigateToFailed;

  static const operationOptions = ['复制 copy（保留原文件）', '移动 move'];
  static const existActionOptions = ['跳过 skip', '覆盖 overwrite', '重命名 rename（_1、_2…）'];
  static const levelOptions = ['按日 2024/01/15', '按月 2024/01', '按年 2024'];

  WorkbenchViewModel({required this.state, required this.dataDir}) {
    final config = state.config;
    sourceDir = config.paths.sourceDir;
    outputDir = config.paths.outputDir;
    pendingDir = config.paths.pendingDir;
    fixMtime = config.execute.fixMtime;
    operationIndex = config.execute.operation == FileOperation.move ? 1 : 0;
    existActionIndex = existActionOptions.indexWhereOption(config.execute.existAction);
    levelIndex = levelIndexFrom(config.execute.classificationLevel);

    _session = OrganizeSession(
      config: config,
      patterns: state.patterns,
      dataDir: dataDir,
      level: config.execute.classificationLevel,
      operation: config.execute.operation,
      existAction: config.execute.existAction,
      fixMtime: config.execute.fixMtime,
    )
      ..onPlanChanged = _onPlanChanged
      ..onAnalysisCompleted = _onAnalysisCompleted;

    reloadOutputTargets();
    // 恢复上次会话的分析结果（跨重启恢复执行，FR-A4.2）
    final restored = AnalysisResultStore.load('$dataDir/analysis-result.json');
    if (restored != null) {
      _session.setResult(restored, displayTarget);
    }
  }

  // ---- 可观察状态 ----
  String sourceDir = '';
  String outputDir = '';
  String pendingDir = '';
  bool isBusy = false;
  /// 忙碌阶段：'' 空闲 / 'analyze' 分析中 / 'execute' 执行中（驱动步骤条与按钮文案）。
  String phase = '';
  double progress = 0; // 0~100
  String statusText = '就绪';
  int totalFiles = 0;
  int successFiles = 0;
  int failedFiles = 0;
  String successRate = '--';
  String sourceSummary = '';
  bool hasResult = false;
  bool canExecute = false;
  String planText = '请先完成分析';
  int operationIndex = 0;
  int existActionIndex = 0;
  int levelIndex = 0;
  bool fixMtime = false;
  bool isMoveAllowed = true;
  int outputTargetIndex = 0;
  List<String> outputTargetOptions = ['本地目录'];

  /// 分析完成回调（供外壳刷新失败列表/角标/报告）。
  void Function(AnalysisResult result)? onAnalysisCompletedExternal;

  void setSourceDir(String v) {
    if (sourceDir == v) return;
    sourceDir = v;
    state.config.paths.sourceDir = v;
    state.saveConfig(notifyChanged: false);
    notifyListeners();
  }

  void setOutputDir(String v) {
    if (outputDir == v) return;
    outputDir = v;
    state.config.paths.outputDir = v;
    state.saveConfig(notifyChanged: false);
    notifyListeners();
  }

  void setPendingDir(String v) {
    if (pendingDir == v) return;
    pendingDir = v;
    state.config.paths.pendingDir = v;
    state.saveConfig(notifyChanged: false);
    notifyListeners();
  }

  Future<void> browseSource() async {
    final dir = await FolderPicker.pick();
    if (dir != null) setSourceDir(dir);
  }

  Future<void> browseOutput() async {
    final dir = await FolderPicker.pick();
    if (dir != null) setOutputDir(dir);
  }

  Future<void> browsePending() async {
    final dir = await FolderPicker.pick();
    if (dir != null) setPendingDir(dir);
  }

  /// 网络位置列表变化后刷新目标下拉并恢复选择。
  void reloadOutputTargets() {
    final profiles = state.config.networkProfiles;
    outputTargetOptions = [
      '本地目录',
      ...profiles.map((p) => '网络位置：${p.name}'),
    ];
    final selected = state.config.paths.outputNetworkProfile;
    final newIndex = selected.isEmpty
        ? 0
        : () {
            final i = profiles.indexWhere((p) => p.name == selected);
            return i < 0 ? 0 : i + 1;
          }();
    outputTargetIndex = newIndex;
    updateNetworkState();
    notifyListeners();
  }

  void selectOutputTarget(int value) {
    if (value == outputTargetOptions.length && value > 1) {
      // "+ 添加网络位置…"：转设置页，回退选择
      outputTargetIndex = outputTargetIndex.clamp(0, outputTargetOptions.length - 1);
      notifyListeners();
      onNavigateToSettings?.call();
      return;
    }
    outputTargetIndex = value;
    final profiles = state.config.networkProfiles;
    state.config.paths.outputNetworkProfile =
        value > 0 && value - 1 < profiles.length ? profiles[value - 1].name : '';
    state.saveConfig(notifyChanged: false); // 仅持久化目标，不失效计划
    updateNetworkState();
    notifyListeners();
  }

  void updateNetworkState() {
    final isNetwork = outputTargetIndex > 0;
    isMoveAllowed = !isNetwork;
    if (isNetwork && operationIndex == 1) {
      operationIndex = 0; // 网络目标仅 copy
    }
    final result = _session.lastResult;
    if (result != null) {
      _session.setResult(result, displayTarget);
    }
  }

  String get displayTarget {
    final idx = outputTargetIndex;
    final profiles = state.config.networkProfiles;
    if (idx > 0 && idx - 1 < profiles.length) {
      return '网络位置：${profiles[idx - 1].name}（仅复制）';
    }
    return outputDir;
  }

  void _onPlanChanged(ArchivePlan plan) {
    final op = _session.operation == FileOperation.move ? '移动' : '复制';
    planText = '将对 ${plan.files.length} 个文件执行「$op」到 ${_session.currentPlan?.outputRoot ?? ''}';
    canExecute = true;
    notifyListeners();
  }

  void _onAnalysisCompleted(AnalysisResult result) {
    totalFiles = result.total;
    successFiles = result.parsed.length;
    failedFiles = result.unparsed.length;
    successRate = formatP1(result.successRate);
    final bySource = <String, int>{};
    for (final p in result.parsed) {
      bySource[p.source] = (bySource[p.source] ?? 0) + 1;
    }
    final entries = bySource.entries.toList()
      ..sort((a, b) => b.value.compareTo(a.value));
    sourceSummary = entries.map((e) => '${e.key} ${formatN0(e.value)}').join(' · ');
    statusText =
        '分析完成：${result.total} 个文件，成功 ${result.parsed.length}，失败 ${result.unparsed.length}';
    hasResult = true;
    notifyListeners();
    onAnalysisCompletedExternal?.call(result);
  }

  void setOperationIndex(int v) {
    operationIndex = v;
    notifyListeners();
  }

  void setExistActionIndex(int v) {
    existActionIndex = v;
    notifyListeners();
  }

  void setLevelIndex(int v) {
    levelIndex = v;
    // 级别变更即时重建计划
    _session.level = ClassificationLevel.values[v];
    notifyListeners();
  }

  void setFixMtime(bool v) {
    fixMtime = v;
    _session.fixMtime = v;
    notifyListeners();
  }

  /// 一键分析（FR-A4）。
  Future<void> analyze() async {
    if (isBusy) return;

    state.config.paths.sourceDir = sourceDir;
    state.config.paths.outputDir = outputDir;
    isBusy = true;
    phase = 'analyze';
    canExecute = false;
    progress = 0;
    final token = CancelToken();
    _cancelToken = token;
    notifyListeners();
    try {
      await _session.analyze(
        sourceDir,
        displayTarget,
        onProgress: (p) {
          totalFiles = p.total;
          successFiles = p.succeeded;
          failedFiles = p.failed;
          progress = p.total == 0 ? 0 : p.processed / p.total * 100;
          statusText = '正在分析 ${p.processed}/${p.total}…';
          notifyListeners();
        },
        cancelToken: token,
      );
    } on AnalysisCancelledException {
      statusText = '分析已取消';
    } catch (e) {
      statusText = '分析失败：$e';
    } finally {
      isBusy = false;
      phase = '';
      _cancelToken = null;
      notifyListeners();
    }
  }

  void cancelAnalysis() {
    _cancelToken?.cancel();
    statusText = '正在取消…';
    notifyListeners();
  }

  /// 执行当前计划（FR-A5）：执行前快照选项进配置。
  Future<void> executeFiles() async {
    final session = _session;
    if (!session.hasResult || isBusy) return;

    session.operation = operationIndex == 1 ? FileOperation.move : FileOperation.copy;
    session.existAction = ExistAction.values[existActionIndex.clamp(0, 2)];
    session.level = ClassificationLevel.values[levelIndex.clamp(0, 2)];
    session.fixMtime = fixMtime;

    state.config.execute.operation = session.operation;
    state.config.execute.existAction = session.existAction;
    state.config.execute.classificationLevel = session.level;
    state.config.execute.fixMtime = fixMtime;
    // 仅持久化执行偏好，不触发 Changed → 不失效当前计划
    state.saveConfig(notifyChanged: false);

    isBusy = true;
    phase = 'execute';
    progress = 0;
    final token = CancelToken();
    _cancelToken = token;
    notifyListeners();
    try {
      final result = await session.execute(
        onProgress: (p) {
          progress = p * 100;
          statusText = '正在执行 ${(p * 100).toStringAsFixed(0)}%…';
          notifyListeners();
        },
        cancelToken: token,
      );
      statusText =
          '执行完成：成功 ${result.succeeded}，跳过 ${result.skipped}，覆盖 ${result.overwritten}，重命名 ${result.renamed}，失败 ${result.failed}';
    } on OperationCancelledException {
      statusText = '执行已取消';
    } catch (e) {
      statusText = '执行失败：$e';
    } finally {
      isBusy = false;
      phase = '';
      _cancelToken = null;
      notifyListeners();
    }
  }

  /// 取消执行阶段。
  void cancelExecute() => cancelAnalysis();

  /// 规则或配置变更后重建提取链并失效旧计划（由 AppState.Changed 接线）。
  void rebuildChain() {
    if (hasResult) {
      _session.invalidateResult();
      hasResult = false;
      canExecute = false;
      planText = '提取配置已变更，请重新分析后再执行';
      statusText = '提取配置已变更，请重新分析后再执行';
      notifyListeners();
    }
  }
}

// ---- 枚举 ↔ 下标映射 ----
extension _ExistActionIndex on List<String> {
  int indexWhereOption(ExistAction action) => switch (action) {
        ExistAction.overwrite => 1,
        ExistAction.rename => 2,
        ExistAction.skip => 0,
      };
}

int levelIndexFrom(ClassificationLevel l) => switch (l) {
      ClassificationLevel.year => 2,
      ClassificationLevel.month => 1,
      ClassificationLevel.day => 0,
    };
