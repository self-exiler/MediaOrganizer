/// 目标存储抽象（对应 Core/Storage/IFileStorage.cs）。
/// 本地文件系统全量实现；WebDAV 见 services/webdav_storage.dart；
/// SMB 在 Flutter 版未实现（配置可保存，连接测试明确提示）。
library;

import 'dart:async';
import 'dart:io';

/// 目标端文件操作。所有相对路径统一以 '/' 分隔。
abstract class FileStorage {
  /// 是否为网络目标（决定执行默认并行度与 move 可用性）。
  bool get isNetwork;

  Future<bool> exists(String relativePath);

  Future<int> getLength(String relativePath); // 不存在返回 -1

  Future<void> delete(String relativePath);

  /// 递归创建目录。
  Future<void> createDirectory(String relativeDir);

  /// 把本地源文件流式复制到目标的 relativeTarget（不处理同名冲突）。
  Future<void> copyFromLocal(File source, String relativeTarget,
      {void Function(int bytesDone)? onProgress});

  /// 临时名 → 最终名（同目录改名）。
  Future<void> move(String fromRelative, String toRelative);

  /// 设置修改时间；目标不支持时静默跳过（FR-A5.5）。
  Future<void> setModifiedUtc(String relativePath, DateTime utc);
}

/// 本地目录实现。
class LocalFileStorage implements FileStorage {
  final String root;
  @override
  final bool isNetwork = false;

  LocalFileStorage(this.root);

  File _resolve(String relative) {
    final norm = relative.replaceAll('/', Platform.pathSeparator);
    return File(norm.startsWith(Platform.pathSeparator)
        ? norm
        : root.endsWith(Platform.pathSeparator)
            ? '$root$norm'
            : '$root${Platform.pathSeparator}$norm');
  }

  Directory _resolveDir(String relative) {
    final norm = relative.replaceAll('/', Platform.pathSeparator);
    final base = root.endsWith(Platform.pathSeparator)
        ? root.substring(0, root.length - 1)
        : root;
    return Directory(norm.isEmpty ? base : '$base${Platform.pathSeparator}$norm');
  }

  @override
  Future<bool> exists(String relativePath) async => _resolve(relativePath).exists();

  @override
  Future<int> getLength(String relativePath) async {
    try {
      return await _resolve(relativePath).length();
    } on FileSystemException {
      return -1;
    }
  }

  @override
  Future<void> delete(String relativePath) async {
    final f = _resolve(relativePath);
    if (await f.exists()) await f.delete();
  }

  @override
  Future<void> createDirectory(String relativeDir) async =>
      _resolveDir(relativeDir).create(recursive: true);

  @override
  Future<void> copyFromLocal(File source, String relativeTarget,
      {void Function(int bytesDone)? onProgress}) async {
    final target = _resolve(relativeTarget);
    final sink = target.openWrite();
    try {
      await sink.addStream(source.openRead().map((chunk) {
        onProgress?.call(chunk.length);
        return chunk;
      }));
      await sink.flush();
    } finally {
      await sink.close();
    }
  }

  @override
  Future<void> move(String fromRelative, String toRelative) async =>
      _resolve(fromRelative).rename(_resolve(toRelative).path);

  @override
  Future<void> setModifiedUtc(String relativePath, DateTime utc) async {
    final f = _resolve(relativePath);
    if (await f.exists()) {
      try {
        f.setLastModifiedSync(utc.toLocal());
      } on FileSystemException {
        // 只读介质等场景静默跳过
      }
    }
  }
}

/// 文件名查重帮助：stem_n.ext 规则（对应 Core/Execution/NameCollisionResolver.cs）。
class NameCollisionResolver {
  /// 返回第一个不存在的 stem_n.ext 相对路径。
  static Future<String> findFree(
      FileStorage target, String relativePath) async {
    if (!await target.exists(relativePath)) return relativePath;

    final slash = relativePath.lastIndexOf('/');
    final dir = slash < 0 ? '' : relativePath.substring(0, slash);
    final name = _stem(relativePath.substring(slash + 1));
    final ext = _ext(relativePath);
    for (var i = 1;; i++) {
      final candidate = dir.isEmpty ? '${name}_$i$ext' : '$dir/${name}_$i$ext';
      if (!await target.exists(candidate)) return candidate;
    }
  }

  static String _stem(String fileName) {
    final dot = fileName.lastIndexOf('.');
    return dot <= 0 ? fileName : fileName.substring(0, dot);
  }

  static String _ext(String fileName) {
    final dot = fileName.lastIndexOf('.');
    return dot <= 0 ? '' : fileName.substring(dot);
  }
}
