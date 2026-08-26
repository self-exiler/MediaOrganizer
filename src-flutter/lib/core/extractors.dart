/// 日期提取链（对应 Core/Extraction/*）：
/// 按权重降序依次尝试启用的提取器，取第一个通过 DateRangeValidator 校验的结果
/// （SRS FR-A2.2）。默认权重 Exif 1.2 > FileName 1.1 > FileSystem 0.8。
library;

import 'dart:io';

import 'app_config.dart';
import 'models.dart';
import 'mp4_date_reader.dart';
import 'package:exif/exif.dart' as exif_pkg;
import 'pattern_engine.dart';
import 'patterns_store.dart';

/// 抽象提取器。
abstract class DateExtractor {
  String get name;
  bool get enabled;
  double get weight;

  /// 返回 null 表示该提取器无结果。
  Future<DateTime?> extract(MediaFile file);
}

/// 日期统一校验：1970-01-01 ~ 2100-12-31；不早于（当前年份 − maxYearsPast）；
/// 不晚于（当前 + futureDateBufferDays）（SRS FR-A2.4）。
class DateRangeValidator {
  final int maxYearsPast;
  final int futureDateBufferDays;
  final DateTime now;

  DateRangeValidator({
    this.maxYearsPast = 30,
    this.futureDateBufferDays = 0,
    DateTime? now,
  })  : assert(maxYearsPast >= 0, 'must be non-negative'),
        assert(futureDateBufferDays >= 0, 'must be non-negative'),
        now = now ?? DateTime.now();

  bool isValid(DateTime date) {
    if (date.year < 1970 || date.year > 2100) return false;
    final earliest = DateTime(now.year - maxYearsPast, 1, 1);
    final latest = now.add(Duration(days: futureDateBufferDays));
    return !date.isBefore(earliest) && !date.isAfter(latest);
  }
}

/// EXIF 提取器（FR-A2.1/A2.3）：图片读 EXIF 日期；视频读 MP4/MOV 容器 mvhd 创建时间。
/// HEIC/PNG 等纯 Dart EXIF 覆盖受限的格式自然落入文件名提取器兜底（SRS R-A1 缓解策略）。
class ExifExtractor extends DateExtractor {
  @override
  String get name => 'Exif';
  @override
  final bool enabled;
  @override
  final double weight;

  ExifExtractor({required this.enabled, required this.weight});

  static const _videoExtensions = {
    '.mp4', '.mov', '.avi', '.mkv', '.wmv', '.flv', '.webm',
    '.m4v', '.mpg', '.mpeg', '.3gp', '.3g2',
  };

  @override
  Future<DateTime?> extract(MediaFile file) async {
    final dot = file.fileName.lastIndexOf('.');
    final ext = dot < 0 ? '' : file.fileName.substring(dot).toLowerCase();
    return _videoExtensions.contains(ext)
        ? extractFromVideo(file.path)
        : extractFromImage(file.path);
  }

  static Future<DateTime?> extractFromImage(String path) async {
    try {
      // package:exif 的 fromBytes 仅解析头部段；限制超大文件以防内存峰值
      final f = File(path);
      if (f.lengthSync() > 128 * 1024 * 1024) return null;
      final tags = await exif_pkg.Exif.fromBytes(f.readAsBytesSync());
      return _parseExifDate([
        _text(tags['EXIF DateTimeOriginal']),
        _text(tags['EXIF SubSecDateTimeOriginal']),
        _text(tags['EXIF DateTimeDigitized']),
        _text(tags['Image DateTime']),
      ]);
    } catch (_) {
      // 损坏文件/无权限等：一律视为该提取器无结果（NFR-A4）
      return null;
    }
  }

  /// package:exif 的 ExifTag.toString() 输出可读文本，直接经此取值最稳妥。
  static String? _text(Object? tag) {
    if (tag == null) return null;
    final s = tag.toString().trim();
    return s.isEmpty ? null : s;
  }

  /// 解析 "yyyy:MM:dd HH:mm:ss" 及常见变体。
  static DateTime? _parseExifDate(List<String?> candidates) {
    for (final raw in candidates) {
      final s = raw?.trim();
      if (s == null || s.isEmpty) continue;
      // EXIF 惯例 "2024:01:15 10:30:00" → 先归一为 ISO 形式再尝试解析
      final normalized = s.replaceFirstMapped(
        RegExp(r'^(\d{4}):(\d{2}):(\d{2})'),
        (m) => '${m[1]}-${m[2]}-${m[3]}',
      );
      var dt = _parseWithoutTimezone(normalized);
      if (dt == null) {
        final m = RegExp(
                r'(\d{4})-(\d{2})-(\d{2})[T ](\d{1,2}):(\d{2})(:(\d{2}))?')
            .firstMatch(normalized);
        if (m != null) {
          dt = DateTime(
            int.parse(m.group(1)!),
            int.parse(m.group(2)!),
            int.parse(m.group(3)!),
            int.parse(m.group(4)!),
            int.parse(m.group(5)!),
            m.group(7) == null ? 0 : int.parse(m.group(7)!),
          );
        }
      }
      if (dt != null) return dt;
    }
    return null;
  }

  /// 剥离时区/亚秒后缀后按本地时间解析（与 C# 版 Unspecified + 本地偏移语义一致）。
  static DateTime? _parseWithoutTimezone(String s) {
    final m =
        RegExp(r'^(\d{4})-(\d{2})-(\d{2})[T ](\d{1,2}):(\d{2})(?::(\d{2}))?')
            .firstMatch(s);
    if (m == null) return null;
    return DateTime(
      int.parse(m.group(1)!),
      int.parse(m.group(2)!),
      int.parse(m.group(3)!),
      int.parse(m.group(4)!),
      int.parse(m.group(5)!),
      m.group(6) == null ? 0 : int.parse(m.group(6)!),
    );
  }

  static Future<DateTime?> extractFromVideo(String path) async {
    try {
      return Mp4DateReader.readCreationTime(path);
    } catch (_) {
      return null; // 容器无法解析等：视为无结果
    }
  }
}

/// 文件名提取器：用正则模式集合解析文件名中的日期。
class FileNameExtractor extends DateExtractor {
  @override
  String get name => 'FileName';
  @override
  final bool enabled;
  @override
  final double weight;
  final List<PatternDefinition> patterns;

  FileNameExtractor(this.patterns, {required this.enabled, required this.weight});

  @override
  Future<DateTime?> extract(MediaFile file) async {
    final name = file.fileName;
    // 快速年份预扫描：无合理年份且无时间戳模式时直接跳过全部正则（SRS FR-3.3）
    if (!PatternEngine.containsLikelyDate(name, patterns)) return null;
    return PatternEngine.tryExtract(name, patterns);
  }
}

/// 文件系统提取器：以文件修改时间（mtime）兜底。默认禁用。
class FileSystemExtractor extends DateExtractor {
  @override
  String get name => 'FileSystem';
  @override
  final bool enabled;
  @override
  final double weight;

  FileSystemExtractor({required this.enabled, required this.weight})
      : assert(weight >= 0);

  @override
  Future<DateTime?> extract(MediaFile file) async => file.modified;
}

/// 加权提取链。
class ExtractorChain {
  final DateRangeValidator _validator;
  late final List<DateExtractor> _extractors;

  ExtractorChain(List<DateExtractor> extractors, this._validator) {
    _extractors = [...extractors]
      ..sort((a, b) => b.weight.compareTo(a.weight));
  }

  /// 按配置 + 模式集合构建标准三条提取器链（对应 ExtractorChain.FromConfig）。
  factory ExtractorChain.fromConfig(
    ExtractionConfig cfg,
    List<PatternDefinition> patterns, {
    DateTime? now,
  }) {
    (bool, double) pick(String name, bool defEnabled, double defWeight) {
      final s = cfg.setting(name);
      return (s?.enabled ?? defEnabled, s?.weight ?? defWeight);
    }

    final (exifOn, exifW) = pick('Exif', true, 1.2);
    final (fnOn, fnW) = pick('FileName', true, 1.1);
    final (fsOn, fsW) = pick('FileSystem', false, 0.8);

    return ExtractorChain([
      ExifExtractor(enabled: exifOn, weight: exifW),
      FileNameExtractor(patterns, enabled: fnOn, weight: fnW),
      FileSystemExtractor(enabled: fsOn, weight: fsW),
    ], DateRangeValidator(
      maxYearsPast: cfg.maxYearsPast,
      futureDateBufferDays: cfg.futureDateBufferDays,
      now: now,
    ));
  }

  Future<ExtractResult?> tryExtract(MediaFile file) async {
    for (final extractor in _extractors) {
      if (!extractor.enabled) continue;
      final date = await extractor.extract(file);
      if (date != null && _validator.isValid(date)) {
        return ExtractResult(date, extractor.name);
      }
    }
    return null;
  }
}
