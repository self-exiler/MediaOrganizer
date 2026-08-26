/// 递归扫描源目录（对应 Core/Scanning/FileScanner.cs）：
/// 按扩展名白名单过滤；scanAllFiles 或空白名单 = 全收。在分析 Isolate 内同步执行。
library;

import 'dart:io';

import 'models.dart';

class FileScanner {
  final Set<String> _formats;
  final bool scanAllFiles;

  FileScanner(Iterable<String> supportedFormats, {this.scanAllFiles = false})
      : _formats = supportedFormats
            .map((f) => f.trim().replaceAll('.', '').toLowerCase())
            .where((f) => f.isNotEmpty)
            .toSet();

  /// 返回媒体文件列表；单个文件 stat 失败（扫描中被占用/删除）时跳过（NFR-A4）。
  List<MediaFile> scan(String sourceDir) {
    final dir = Directory(sourceDir);
    if (sourceDir.trim().isEmpty || !dir.existsSync()) return [];

    final list = <MediaFile>[];
    final entities = dir.listSync(recursive: true, followLinks: false);
    for (final e in entities) {
      if (e is! File) continue;
      final name = e.uri.pathSegments.where((s) => s.isNotEmpty).last;
      final dot = name.lastIndexOf('.');
      final ext = dot < 0 ? '' : name.substring(dot + 1).toLowerCase();
      if (!scanAllFiles && _formats.isNotEmpty && !_formats.contains(ext)) {
        continue;
      }
      try {
        final st = e.statSync();
        if (st.type != FileSystemEntityType.file) continue;
        list.add(MediaFile(
          path: e.path,
          size: st.size,
          extension: ext,
          modified: st.modified,
        ));
      } catch (_) {
        // 文件可能在扫描过程中被占用/删除，跳过
      }
    }
    return list;
  }
}
