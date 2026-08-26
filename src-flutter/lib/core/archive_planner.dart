/// 归档规划器（对应 Core/Planning/ArchivePlanner.cs）：
/// 日期 → 分级目标相对路径，未来日期归入 FutureDate/（SRS FR-A5.1/A5.4）。
library;

import 'enums.dart';
import 'models.dart';

class ArchivePlanner {
  final ClassificationLevel level;
  final int futureDateBufferDays;
  final DateTime? now;

  ArchivePlanner(this.level, {this.futureDateBufferDays = 0, this.now});

  ArchivePlan plan(AnalysisResult result, String outputRoot) {
    final n = now ?? DateTime.now();
    final files = <PlannedFile>[];
    for (final parsed in result.parsed) {
      final rel = _buildRelative(parsed.date, n);
      // 计划内统一使用 '/' 分隔；执行时按目标存储语义处理
      files.add(PlannedFile(parsed.file, parsed.date, '$rel/${parsed.file.fileName}'));
    }
    return ArchivePlan(result, outputRoot, level, files);
  }

  String _buildRelative(DateTime date, DateTime now) {
    final sub = switch (level) {
      ClassificationLevel.year => '${date.year.toString().padLeft(4, '0')}',
      ClassificationLevel.month =>
        '${date.year.toString().padLeft(4, '0')}/${date.month.toString().padLeft(2, '0')}',
      ClassificationLevel.day =>
        '${date.year.toString().padLeft(4, '0')}/${date.month.toString().padLeft(2, '0')}/${date.day.toString().padLeft(2, '0')}',
    };
    // 与 DateRangeValidator 一致：仅超出 now+缓冲 的日期归入 FutureDate/
    final futureCutoff = DateTime(now.year, now.month, now.day)
        .add(Duration(days: futureDateBufferDays));
    return date.isAfter(futureCutoff) ? 'FutureDate/$sub' : sub;
  }
}
