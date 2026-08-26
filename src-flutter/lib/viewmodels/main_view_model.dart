/// 应用外壳 VM（对应 Android/ViewModels/MainViewModel.cs，FR-A7）：
/// AppBar 标题随页面切换、抽屉导航（整理工作台/失败文件/分析报告/设置）、
/// 底部状态栏聚合操作反馈（忙碌圆点 + 状态文本 + 上次分析摘要）。
library;

import 'package:flutter/foundation.dart';

import '../core/app_state.dart';
import '../core/format.dart';
import 'failed_files_view_model.dart';
import 'report_view_model.dart';
import 'settings_view_model.dart';
import 'workbench_view_model.dart';

/// 抽屉导航项：标签 + 页索引 + 失败文件角标（badge=0 时角标隐藏）。
class NavItem {
  final String label;
  final int page;
  final int badge;

  const NavItem(this.label, this.page, {this.badge = 0});

  bool get hasBadge => badge > 0;

  NavItem withBadge(int b) => NavItem(label, page, badge: b);
}

class MainViewModel extends ChangeNotifier {
  final AppState state;
  late final WorkbenchViewModel workbench;
  late final FailedFilesViewModel failedFiles;
  late final ReportViewModel report;
  late final SettingsViewModel settings;

  int selectedPage = 0; // 0 工作台 / 1 失败 / 2 报告 / 3 设置
  String appTitle = '整理工作台';
  int failedBadge = 0;
  String statusText = '就绪';
  String statusDetail = '尚未分析';
  bool isBusy = false;

  MainViewModel({required this.state, required String dataDir}) {
    workbench = WorkbenchViewModel(state: state, dataDir: dataDir);
    failedFiles = FailedFilesViewModel(state: state);
    report = ReportViewModel(dataDir: dataDir);
    settings = SettingsViewModel(state: state);

    // 工作台分析完成 → 刷新失败列表/角标/报告 + 上次分析摘要
    workbench.onAnalysisCompletedExternal = (result) {
      _refreshAfterAnalysis(result);
    };
    workbench.addListener(_mirrorWorkbenchStatus);

    // 配置/模式变更 → 工作台重建提取链；网络位置列表 → 刷新目标下拉
    state.addListener(() {
      workbench.rebuildChain();
      workbench.reloadOutputTargets();
    });
  }

  List<NavItem> get navItems => [
        const NavItem('整理工作台', 0),
        NavItem('失败文件', 1, badge: failedBadge),
        const NavItem('分析报告', 2),
        const NavItem('设置', 3),
      ];

  void navigate(int page) {
    if (selectedPage == page) return;
    selectedPage = page;
    appTitle = switch (page) {
      1 => '失败文件',
      2 => '分析报告',
      3 => '设置',
      _ => '整理工作台',
    };
    notifyListeners();
  }

  void _refreshAfterAnalysis(AnalysisResult result) {
    failedFiles.refresh(result);
    report.set(result, workbench.displayTarget);
    failedBadge = result.unparsed.length;
    statusDetail =
        '上次分析: ${formatN0(result.total)} 文件 · 成功 ${formatN0(result.parsed.length)}';
    notifyListeners();
  }

  /// 工作台状态镜像到底部状态栏。
  void _mirrorWorkbenchStatus() {
    statusText = workbench.statusText;
    final busy = workbench.isBusy;
    if (isBusy && !busy) {
      statusDetail = workbench.hasResult
          ? '上次分析: ${formatN0(workbench.totalFiles)} 文件 · 成功 ${formatN0(workbench.successFiles)}'
          : statusDetail;
    }
    isBusy = busy;
    notifyListeners();
  }

  @override
  void dispose() {
    workbench.removeListener(_mirrorWorkbenchStatus);
    super.dispose();
  }
}
