/// 分析结果的 JSON 持久化（analysis-result.json，对应 Core/Analysis/AnalysisResultStore.cs）。
/// 读写对称，支持跨会话恢复；path 存真实路径（Flutter 版统一本地路径）。
library;

import 'app_config.dart' show JsonFileStore;
import 'models.dart';

class AnalysisResultStore {
  /// 落盘结构：{Version, SourceDir, AnalyzedAt, Parsed:[{Path,Date,Source,Size}], Unparsed:[...]}
  static void save(String path, AnalysisResult result) {
    JsonFileStore.save(path, <String, Object?>{
      'Version': 1,
      'SourceDir': result.sourceDir,
      'AnalyzedAt': result.analyzedAt.toIso8601String(),
      'Parsed': [
        for (final p in result.parsed)
          {
            'Path': p.file.path,
            'Date': p.date.toIso8601String(),
            'Source': p.source,
            'Size': p.file.size,
          },
      ],
      'Unparsed': [
        for (final u in result.unparsed)
          {'Path': u.file.path, 'Reason': u.reason},
      ],
    });
  }

  /// 从落盘 JSON 恢复分析结果；文件缺失/损坏返回 null。
  static AnalysisResult? load(String path) {
    final file = JsonFileStore.load(path);
    if (file == null) return null;
    List<Map<String, Object?>> listOf(String key) =>
        ((file[key] as List<Object?>?) ?? const [])
            .whereType<Map<String, Object?>>()
            .toList();

    final parsed = listOf('Parsed')
        .map((e) => ParsedFile(
              MediaFile(
                path: e['Path'] as String? ?? '',
                size: (e['Size'] as num?)?.toInt() ?? 0,
                extension: extensionOf(e['Path'] as String? ?? ''),
                modified: DateTime.fromMillisecondsSinceEpoch(0),
              ),
              DateTime.tryParse(e['Date'] as String? ?? '') ??
                  DateTime.fromMillisecondsSinceEpoch(0),
              e['Source'] as String? ?? '',
            ))
        .toList();
    final unparsed = listOf('Unparsed')
        .map((e) => UnparsedFile(
              MediaFile(
                path: e['Path'] as String? ?? '',
                size: 0,
                extension: extensionOf(e['Path'] as String? ?? ''),
                modified: DateTime.fromMillisecondsSinceEpoch(0),
              ),
              e['Reason'] as String? ?? '',
            ))
        .toList();
    return AnalysisResult(
      file['SourceDir'] as String? ?? '',
      DateTime.tryParse(file['AnalyzedAt'] as String? ?? '') ??
          DateTime.fromMillisecondsSinceEpoch(0),
      parsed,
      unparsed,
    );
  }

  static String extensionOf(String path) {
    final name = path.replaceAll('\\', '/');
    final dot = name.lastIndexOf('.');
    final slash = name.lastIndexOf('/');
    if (dot <= slash) return '';
    return name.substring(dot + 1).toLowerCase();
  }
}
