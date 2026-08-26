/// 分析编排（对应 Core/Analysis/Analyzer.cs）：
/// 扫描 → 提取 → 汇总。整个流水线运行在独立 Isolate 中，主线程零阻塞；
/// 进度按 interval 节流上报；取消 = 直接终止 Isolate。
library;

import 'dart:async';
import 'dart:isolate';

import 'app_config.dart';
import 'extractors.dart';
import 'file_scanner.dart';
import 'models.dart';
import 'patterns_store.dart';

class Analyzer {
  /// 启动分析并返回可取消的运行句柄。取消后 result 以
  /// [AnalysisCancelledException] 完成（即使取消发生在 Isolate 就绪前）。
  static AnalyzerRun run({
    required String sourceDir,
    required ScanConfig scan,
    required ExtractionConfig extraction,
    required List<PatternDefinition> patterns,
    void Function(AnalysisProgress progress)? onProgress,
  }) {
    final completer = Completer<AnalysisResult>();
    final port = ReceivePort();
    final errorPort = ReceivePort();
    late final AnalyzerRun run;

    void finishPort() {
      port.close();
      errorPort.close();
    }

    errorPort.listen((message) {
      if (!completer.isCompleted) {
        final detail =
            message is List && message.isNotEmpty ? '${message[0]}' : '$message';
        completer.completeError(StateError('分析失败：$detail'));
      }
      finishPort();
    });

    port.listen((message) {
      if (message is! Map<String, Object?>) return;
      switch (message['type']) {
        case 'progress':
          onProgress?.call(AnalysisProgress(
            (message['processed'] as num).toInt(),
            (message['total'] as num).toInt(),
            (message['succeeded'] as num).toInt(),
            (message['failed'] as num).toInt(),
          ));
        case 'done':
          if (!completer.isCompleted) {
            completer.complete(_resultFromJson(message));
          }
          finishPort();
      }
    });

    run = AnalyzerRun._(completer, finishPort);

    final params = <String, Object?>{
      'sourceDir': sourceDir,
      'scan': scan.toJson(),
      'extraction': extraction.toJson(),
      'patterns': [for (final p in patterns) p.toJson()],
      'port': port.sendPort,
    };

    Future<void> spawnAndAttach() async {
      try {
        final isolate =
            await Isolate.spawn(_workerEntry, params, onError: errorPort.sendPort);
        // spawn 期间已被取消：立即终止（错误已由 cancel 投递）
        if (!run.cancelled) run.attach(isolate);
      } catch (e) {
        if (!completer.isCompleted) {
          completer.completeError(StateError('分析启动失败：$e'));
        }
        finishPort();
      }
    }

    unawaited(spawnAndAttach());
    return run;
  }

  static Future<void> _workerEntry(Map<String, Object?> params) async {
    final port = params['port'] as SendPort;
    final sourceDir = params['sourceDir'] as String;
    final scan = ScanConfig.fromJson(
        ((params['scan'] as Map?) ?? const {}).cast<String, Object?>());
    final extraction = ExtractionConfig.fromJson(
        ((params['extraction'] as Map?) ?? const {}).cast<String, Object?>());
    final patterns = ((params['patterns'] as List<Object?>?) ?? const [])
        .whereType<Map<String, Object?>>()
        .map(PatternDefinition.fromJson)
        .toList();

    final files = FileScanner(scan.supportedFormats,
            scanAllFiles: scan.scanAllFiles)
        .scan(sourceDir);
    final chain = ExtractorChain.fromConfig(extraction, patterns);
    final interval = scan.progressInterval <= 0 ? 10 : scan.progressInterval;

    final parsed = <ParsedFile>[];
    final unparsed = <UnparsedFile>[];
    final total = files.length;
    var processed = 0;

    for (final file in files) {
      final result = await chain.tryExtract(file);
      if (result != null) {
        parsed.add(ParsedFile(file, result.date, result.source));
      } else {
        unparsed.add(UnparsedFile(file, 'NoValidDate'));
      }
      processed++;
      if (processed % interval == 0 || processed == total) {
        port.send({
          'type': 'progress',
          'processed': processed,
          'total': total,
          'succeeded': parsed.length,
          'failed': unparsed.length,
        });
      }
    }

    port.send({
      'type': 'done',
      'sourceDir': sourceDir,
      'analyzedAt': DateTime.now().toIso8601String(),
      'parsed': [
        for (final p in parsed)
          {
            'file': p.file.toJson(),
            'date': p.date.toIso8601String(),
            'source': p.source,
          },
      ],
      'unparsed': [
        for (final u in unparsed)
          {'file': u.file.toJson(), 'reason': u.reason},
      ],
    });
  }

  /// 从 Isolate 传回的 JSON 重建 AnalysisResult。
  static AnalysisResult _resultFromJson(Map<String, Object?> m) {
    final parsed = ((m['parsed'] as List<Object?>?) ?? const [])
        .whereType<Map<String, Object?>>()
        .map((e) => ParsedFile(
              MediaFile.fromJson(
                  (e['file'] as Map).cast<String, Object?>()),
              DateTime.parse(e['date'] as String),
              e['source'] as String? ?? '',
            ))
        .toList();
    final unparsed = ((m['unparsed'] as List<Object?>?) ?? const [])
        .whereType<Map<String, Object?>>()
        .map((e) => UnparsedFile(
              MediaFile.fromJson(
                  (e['file'] as Map).cast<String, Object?>()),
              e['reason'] as String? ?? '',
            ))
        .toList();
    return AnalysisResult(
      m['sourceDir'] as String? ?? '',
      DateTime.tryParse(m['analyzedAt'] as String? ?? '') ?? DateTime.now(),
      parsed,
      unparsed,
    );
  }
}

/// 一次可取消的分析运行。cancel() 终止 Isolate 并让 result 以
/// AnalysisCancelledException 完成。
class AnalyzerRun {
  final Completer<AnalysisResult> _completer;
  final void Function() _cleanup;
  Isolate? _isolate;
  bool _cancelled = false;

  late final Future<AnalysisResult> result = _completer.future;

  AnalyzerRun._(this._completer, this._cleanup);

  bool get cancelled => _cancelled;

  void attach(Isolate isolate) => _isolate = isolate;

  void cancel() {
    if (_cancelled) return;
    _cancelled = true;
    _isolate?.kill(priority: Isolate.immediate);
    if (!_completer.isCompleted) {
      _completer.completeError(const AnalysisCancelledException());
    }
    _cleanup();
  }
}
