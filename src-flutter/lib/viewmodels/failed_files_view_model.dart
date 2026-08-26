/// 失败文件 VM（对应 Shared/ViewModels/FailedFilesViewModel.cs，FR-A6）：
/// 按结构指纹聚类排序的列表 + 勾选批量移动到待处理目录 + 系统应用打开。
library;

import 'dart:io';

import 'package:flutter/foundation.dart';

import '../core/app_state.dart';
import '../core/file_operator.dart';
import '../core/format.dart';
import '../core/models.dart';
import '../core/pattern_engine.dart' show structureFingerprint;
import '../core/pending_file_mover.dart';
import '../services/file_storage.dart';
import '../services/platform_services.dart';
import '../services/storage_factory.dart';

/// 失败文件列表项。isChecked 为批量移动的目标选择。
class FailedItem extends ChangeNotifier {
  final UnparsedFile file;
  final String fingerprint;
  bool isChecked = false;

  FailedItem(this.file) : fingerprint = structureFingerprint(file.file.fileName);

  String get name => file.file.fileName;
  String get path => file.file.path;
  int get size => file.file.size;
  String get reason => file.reason;
  String get sizeLabel => formatSize(size);

  void setChecked(bool v) {
    if (isChecked != v) {
      isChecked = v;
      notifyListeners();
    }
  }
}

class FailedFilesViewModel extends ChangeNotifier {
  final AppState state;

  /// 指纹 → 组内条目（保持指纹相邻排序后的分组视图）。
  final List<FailedItem> items = [];
  final expandedPaths = <String>{};

  bool isMoving = false;
  double moveProgress = 0; // 0~100
  String moveHint = '';
  CancelToken? _cancelToken;

  FailedFilesViewModel({required this.state});

  String get pendingDir => state.config.paths.pendingDir;

  int get checkedCount => items.where((i) => i.isChecked).length;

  void refresh(AnalysisResult? result) {
    items.clear();
    expandedPaths.clear();
    moveHint = '';
    if (result == null) {
      notifyListeners();
      return;
    }
    // FR-A6.1：按结构指纹聚类排序（指纹一致的相邻），组内按文件名
    final sorted = [...result.unparsed]
      ..sort((a, b) {
        final fa = structureFingerprint(a.file.fileName);
        final fb = structureFingerprint(b.file.fileName);
        final c = fa.compareTo(fb);
        return c != 0 ? c : a.file.fileName.compareTo(b.file.fileName);
      });
    for (final u in sorted) {
      items.add(FailedItem(u));
    }
    notifyListeners();
  }

  void toggleExpanded(String path) {
    if (!expandedPaths.add(path)) {
      expandedPaths.remove(path);
    }
    notifyListeners();
  }

  void toggleItem(FailedItem item) => item.setChecked(!item.isChecked);

  /// 全选/取消全选：有未勾选项时全部勾选，否则全部取消。
  bool get shouldSelectAll => items.any((i) => !i.isChecked);

  void toggleSelectAll() {
    final check = shouldSelectAll;
    for (final i in items) {
      i.setChecked(check);
    }
    notifyListeners();
  }

  /// 批量移动到待处理目录。确认弹窗由 UI 层完成后调用 [moveConfirmed]。
  List<FailedItem> itemsToMove() {
    final checked = items.where((i) => i.isChecked).toList();
    // 有勾选项时仅移动勾选的；全部未勾选则移动全部（桌面语义）
    return checked.isEmpty ? List.of(items) : checked;
  }

  Future<void> moveConfirmed(List<FailedItem> selected) async {
    if (items.isEmpty || isMoving || selected.isEmpty) return;

    final targetDir = pendingDir;
    if (targetDir.trim().isEmpty) {
      moveHint = '请先在工作台指定「待处理目录」';
      notifyListeners();
      return;
    }

    isMoving = true;
    moveProgress = 0;
    moveHint = '';
    _cancelToken = CancelToken();
    notifyListeners();
    try {
      final target = StorageFactory.createLocalStorage(targetDir);
      final result = await PendingFileMover().move(
        selected.map((i) => i.file.file).toList(),
        target,
        onProgress: (p) {
          moveProgress = p * 100;
          notifyListeners();
        },
        cancelToken: _cancelToken,
      );

      final movedNames = result.movedNames.toSet();
      items.removeWhere((i) => movedNames.contains(i.name));
      moveHint = '移动完成：成功 ${result.moved}，失败 ${result.failed}';
      if (result.errors.isNotEmpty) {
        moveHint += ' · ${result.errors.first}';
      }
    } catch (e) {
      moveHint = '移动失败：$e';
    } finally {
      isMoving = false;
      _cancelToken = null;
      notifyListeners();
    }
  }

  void cancelMove() => _cancelToken?.cancel();

  /// 用系统默认应用打开（FR-A6.3）。
  Future<void> openWithSystem(FailedItem item) async {
    if (!File(item.path).existsSync()) {
      moveHint = '文件不存在或不可访问：${item.path}';
      notifyListeners();
      return;
    }
    await ReportExporter.openWithSystem(item.path);
  }
}
