/// 分析报告查看器 VM（对应 Shared/ViewModels/ReportViewModel.cs）：
/// TXT 已由工作台分析时落盘应用专有目录，此处展示 + 另存为/分享。
library;

import 'dart:io';

import 'package:flutter/foundation.dart';

import '../core/format.dart' show formatN0;
import '../core/models.dart';
import '../core/report_generator.dart';
import '../services/platform_services.dart';

class ReportViewModel extends ChangeNotifier {
  final String dataDir;

  String reportText;
  String reportMeta = '';

  ReportViewModel({required this.dataDir})
      : reportText = '尚未生成报告。请先在工作台完成一次分析。' {
    loadFromDisk();
  }

  bool get hasReport => File('$dataDir/analysis-report.txt').existsSync();

  String get reportPath => '$dataDir/analysis-report.txt';

  /// 应用启动时恢复上次落盘的报告。
  void loadFromDisk() {
    try {
      final f = File(reportPath);
      if (f.existsSync()) {
        reportText = f.readAsStringSync();
      }
    } catch (_) {
      // 读取失败保持占位文案
    }
  }

  void set(AnalysisResult result, String outputDir) {
    reportText = AnalysisReportGenerator.generate(result, outputDir);
    final at = result.analyzedAt;
    final two = (int v) => v.toString().padLeft(2, '0');
    reportMeta =
        '${result.sourceDir} · ${at.year}-${two(at.month)}-${two(at.day)} '
        '${two(at.hour)}:${two(at.minute)}:${two(at.second)}';
    notifyListeners();
  }

  /// 另存为；返回提示消息。
  Future<String> saveReport() async {
    final path = await ReportExporter.saveTextAs('analysis-report.txt', reportText);
    return path == null ? '已取消另存为' : '已保存到 $path';
  }

  /// 系统分享。
  Future<void> shareReport() async {
    if (!hasReport) return;
    await ReportExporter.share('MediaOrganizer 分析报告', reportPath);
  }

  /// 状态栏摘要（外壳用）。
  String summaryFor(AnalysisResult? result) => result == null
      ? '尚未分析'
      : '上次分析: ${formatN0(result.total)} 文件 · 成功 ${formatN0(result.parsed.length)}';
}
