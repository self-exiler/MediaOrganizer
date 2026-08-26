/// 领域模型（对应 Core/Models/MediaFile.cs 与 Planning/Execution 的记录类型）。
///
/// Flutter 版统一走本地真实路径，`MediaFile` 直接携带扫描时预取的
/// 大小与修改时间（FileSystemExtractor 兜底用），不再保留 IMediaSource 抽象。
library;

import 'enums.dart';

/// 扫描到的一个媒体文件（不包含任何提取结果）。
class MediaFile {
  final String path;
  final int size;
  final String extension;
  final DateTime modified;

  const MediaFile({
    required this.path,
    required this.size,
    required this.extension,
    required this.modified,
  });

  String get fileName {
    final p = path.replaceAll('\\', '/');
    return p.substring(p.lastIndexOf('/') + 1);
  }

  Map<String, Object?> toJson() => {
        'path': path,
        'size': size,
        'extension': extension,
        'modified': modified.toIso8601String(),
      };

  static MediaFile fromJson(Map<String, Object?> j) => MediaFile(
        path: j['path'] as String,
        size: (j['size'] as num?)?.toInt() ?? 0,
        extension: (j['extension'] as String?) ?? '',
        modified: DateTime.tryParse((j['modified'] as String?) ?? '') ??
            DateTime.fromMillisecondsSinceEpoch(0),
      );
}

/// 成功提取到日期的文件。
class ParsedFile {
  final MediaFile file;
  final DateTime date;
  final String source; // 提取来源：Exif / FileName / FileSystem

  const ParsedFile(this.file, this.date, this.source);
}

/// 未能提取到日期的文件。
class UnparsedFile {
  final MediaFile file;
  final String reason;

  const UnparsedFile(this.file, this.reason);
}

/// 一次完整分析的结果。
class AnalysisResult {
  final String sourceDir;
  final DateTime analyzedAt;
  final List<ParsedFile> parsed;
  final List<UnparsedFile> unparsed;

  const AnalysisResult(this.sourceDir, this.analyzedAt, this.parsed, this.unparsed);

  int get total => parsed.length + unparsed.length;
  double get successRate => total == 0 ? 0 : parsed.length / total;
}

/// 单个提取器的产出。
class ExtractResult {
  final DateTime date;
  final String source;

  const ExtractResult(this.date, this.source);
}

/// 分析进度上报（节流后）。
class AnalysisProgress {
  final int processed;
  final int total;
  final int succeeded;
  final int failed;

  const AnalysisProgress(this.processed, this.total, this.succeeded, this.failed);
}

/// 分析被用户取消。
class AnalysisCancelledException implements Exception {
  const AnalysisCancelledException();

  @override
  String toString() => '分析已取消';
}

/// 归档计划中的单个文件。relativeTarget 统一使用 '/' 分隔。
class PlannedFile {
  final MediaFile source;
  final DateTime date;
  final String relativeTarget;

  const PlannedFile(this.source, this.date, this.relativeTarget);
}

/// 一次完整归档计划。
class ArchivePlan {
  final AnalysisResult result;
  final String outputRoot;
  final ClassificationLevel level;
  final List<PlannedFile> files;

  const ArchivePlan(this.result, this.outputRoot, this.level, this.files);
}

/// 执行阶段汇总。
class FileOperationResult {
  final int succeeded;
  final int skipped;
  final int overwritten;
  final int renamed;
  final int failed;
  final List<String> errors;

  const FileOperationResult(
      this.succeeded, this.skipped, this.overwritten, this.renamed, this.failed,
      this.errors = const []);
}

/// 失败文件批量移动汇总。
class PendingMoveResult {
  final int moved;
  final int failed;
  final List<String> movedNames;
  final List<String> errors;

  const PendingMoveResult(this.moved, this.failed, this.movedNames, this.errors);
}
