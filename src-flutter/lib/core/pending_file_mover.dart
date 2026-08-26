/// 失败文件批量移动到待处理目录（对应 Core/Execution/PendingFileMover.cs，FR-A6.5）：
/// 复制 + 大小校验 + 删除源；同名冲突沿用 rename `_n` 规则。
/// 逐个移动；单文件失败记录并继续（NFR-A4）。
library;

import 'dart:io';

import '../services/file_storage.dart';
import 'file_operator.dart' show CancelToken, checkCancelled;
import 'models.dart';

class PendingFileMover {
  Future<PendingMoveResult> move(
    List<MediaFile> files,
    FileStorage target, {
    void Function(double progress)? onProgress,
    CancelToken? cancelToken,
  }) async {
    var moved = 0, failed = 0;
    final errors = <String>[];
    final movedNames = <String>[];
    await target.createDirectory('');

    for (var i = 0; i < files.length; i++) {
      checkCancelled(cancelToken);
      final file = files[i];
      try {
        final name = file.fileName;
        final relative =
            await NameCollisionResolver.findFree(target, name);
        await target.copyFromLocal(File(file.path), relative);

        final expected = file.size;
        final actual = await target.getLength(relative);
        if (actual >= 0 && actual != expected) {
          throw StateError('大小校验失败：期望 $expected，实际 $actual');
        }

        await File(file.path).delete();
        moved++;
        movedNames.add(name);
      } on OperationCancelledException {
        rethrow;
      } catch (e) {
        failed++;
        errors.add('${file.fileName}: $e');
      }
      onProgress?.call((i + 1) / files.length);
    }
    return PendingMoveResult(moved, failed, movedNames, errors);
  }
}
