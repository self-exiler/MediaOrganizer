/// 执行引擎（对应 Core/Execution/FileOperator.cs）：
/// copy/move + 同名处理 + mtime 矫正（SRS FR-A5.2/5.3/5.5，ADR-0004）。
/// 网络传输约定与桌面版一致：临时名 `.mo-tmp` → 大小校验 → 改名；
/// 失败重试 ≤3 次（指数退避 1/2/4s）。move = 目标落盘确认后删除源。
library;

import 'dart:async';
import 'dart:io';

import '../services/file_storage.dart';
import 'enums.dart';
import 'models.dart';

class FileOperator {
  static const maxRetries = 3;

  final FileStorage target;
  final FileOperation operation;
  final ExistAction existAction;
  final bool fixMtime;
  final int concurrency;

  FileOperator(
    this.target,
    this.operation,
    this.existAction,
    this.fixMtime, {
    int concurrency = 2,
  }) : concurrency = concurrency <= 0 ? (target.isNetwork ? 4 : 2) : concurrency;

  /// 执行计划；进度按完成文件数上报 0~1。取消经 cancelToken 在文件间/流内协作生效。
  Future<FileOperationResult> execute(
    ArchivePlan plan, {
    void Function(double progress)? onProgress,
    CancelToken? cancelToken,
  }) async {
    var ok = 0, skip = 0, overwrite = 0, rename = 0, fail = 0;
    final errors = <String>[];
    final files = plan.files;
    var processed = 0;

    // 同一原始目标路径的碰撞检查必须互斥：并行下两个源写同一目标会漏判同名
    // （对应 C# 版 per-target gate，这里用 future 链实现异步互斥）
    final gates = <String, Future<void>>{};
    _cursor = 0;

    Future<void> worker() async {
      while (true) {
        if (cancelToken?.isCancelled ?? false) return;
        final index = _nextIndex(files.length);
        if (index == null) return;

        final f = files[index];
        // 与 C# 一致：同目标路径的任务串行化
        while (gates.containsKey(f.relativeTarget)) {
          await gates[f.relativeTarget];
        }
        final gate = Completer<void>();
        gates[f.relativeTarget] = gate.future;
        try {
          final resolution =
              await resolveCollision(f.relativeTarget, cancelToken);
          if (resolution.target == null) {
            switch (resolution.action) {
              case CollisionAction.skip:
                skip++;
              case CollisionAction.overwrite:
                overwrite++;
              case CollisionAction.rename:
                rename++;
            }
          } else {
            if (resolution.action == CollisionAction.overwrite) overwrite++;
            if (resolution.action == CollisionAction.rename) rename++;

            final temp = '${f.relativeTarget}.mo-tmp';
            await transferWithRetry(f, temp, resolution.target!, cancelToken);

            // move 语义：目标确认落盘后删除源（网络目标下 GUI 已禁用 move）
            if (operation == FileOperation.move) {
              try {
                await File(f.source.path).delete();
              } catch (e) {
                errors.add('${f.source.fileName}: 删除源失败 $e');
              }
            }
            ok++;
          }
        } on OperationCancelledException {
          rethrow;
        } catch (e) {
          fail++;
          errors.add('${f.source.fileName} -> ${f.relativeTarget}: $e');
        } finally {
          gates.remove(f.relativeTarget);
          gate.complete();
        }

        processed++;
        onProgress?.call(processed / files.length);
      }
    }

    try {
      await Future.wait(
          List.generate(concurrency.clamp(1, files.isEmpty ? 1 : files.length), (_) => worker()));
    } on OperationCancelledException {
      // 已取消：返回已完成部分
    }

    return FileOperationResult(ok, skip, overwrite, rename, fail, errors);
  }

  int? _nextIndex(int length) => _cursor < length ? _cursor++ : null;
  int _cursor = 0;

  Future<CollisionResolution> resolveCollision(
      String relativeTarget, CancelToken? ct) async {
    if (!await target.exists(relativeTarget)) {
      return CollisionResolution(relativeTarget, CollisionAction.none);
    }
    switch (existAction) {
      case ExistAction.skip:
        return const CollisionResolution(null, CollisionAction.skip);
      case ExistAction.overwrite:
        await target.delete(relativeTarget);
        return CollisionResolution(relativeTarget, CollisionAction.overwrite);
      case ExistAction.rename:
        final free = await NameCollisionResolver.findFree(target, relativeTarget);
        return CollisionResolution(free, CollisionAction.rename);
    }
  }

  Future<void> transferWithRetry(
      PlannedFile f, String temp, String finalTarget, CancelToken? ct) async {
    for (var attempt = 0;; attempt++) {
      checkCancelled(ct);
      try {
        final dir = finalTarget.contains('/')
            ? finalTarget.substring(0, finalTarget.lastIndexOf('/'))
            : '';
        await target.createDirectory(dir);
        await target.copyFromLocal(File(f.source.path), temp,
            onProgress: (_) => checkCancelled(ct));

        // 大小校验（目标获取不到长度时跳过）
        final expected = f.source.size;
        final actual = await target.getLength(temp);
        if (actual >= 0 && actual != expected) {
          throw FileSystemException('大小校验失败', temp,
              OSError('期望 $expected，实际 $actual'));
        }

        await target.move(temp, finalTarget);
        if (fixMtime) {
          await target.setModifiedUtc(finalTarget, f.date.toUtc());
        }
        return;
      } on OperationCancelledException {
        rethrow;
      } catch (_) {
        if (attempt >= maxRetries - 1) rethrow;
        // 指数退避 1/2/4s，等待期间响应取消
        final delayMs = 1000 << attempt.clamp(0, 2);
        await Future.delayed(Duration(milliseconds: delayMs));
        checkCancelled(ct);
      }
    }
  }
}

void checkCancelled(CancelToken? ct) {
  if (ct?.isCancelled ?? false) throw const OperationCancelledException();
}

class OperationCancelledException implements Exception {
  const OperationCancelledException();

  @override
  String toString() => '操作已取消';
}

/// 协作式取消令牌。
class CancelToken {
  bool isCancelled = false;
  final List<void Function()> _listeners = [];

  void cancel() {
    if (isCancelled) return;
    isCancelled = true;
    for (final listener in List.of(_listeners)) {
      listener();
    }
  }

  /// 注册取消回调（如终止分析 Isolate）。
  void onCancel(void Function() callback) => _listeners.add(callback);
}

enum CollisionAction { none, skip, overwrite, rename }

class CollisionResolution {
  final String? target; // null 表示无需传输（跳过等）
  final CollisionAction action;

  const CollisionResolution(this.target, this.action);
}
