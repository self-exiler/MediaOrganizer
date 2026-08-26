/// 文件名模式引擎（对应 Core/Patterns/PatternEngine.cs）：正则匹配 + group_mapping + 时间戳解析。
library;

import 'patterns_store.dart';

class PatternEngine {
  static final RegExp _timestampRegex = RegExp(r'\d{10,}');
  static final RegExp _yearRegex = RegExp(r'19[7-9]\d|20\d\d|21\d\d');

  /// 正则编译缓存：pattern string → compiled RegExp?（非法模式缓存 null）。
  static final Map<String, RegExp?> _regexCache = {};

  static RegExp? _getCachedRegex(String pattern) {
    final cached = _regexCache[pattern];
    if (cached != null || _regexCache.containsKey(pattern)) return cached;
    RegExp? compiled;
    try {
      compiled = RegExp(pattern, unicode: true);
    } on FormatException {
      compiled = null;
    }
    _regexCache[pattern] = compiled;
    return compiled;
  }

  /// 快速预扫描：文件名中是否可能存在日期（合理年份或 ≥10 位连续数字时间戳），
  /// 避免对每个文件跑全部正则。
  static bool containsLikelyDate(String fileName, List<PatternDefinition> patterns) {
    if (patterns.any((p) => p.enabled && p.timestampLength != null) &&
        _timestampRegex.hasMatch(fileName)) {
      return true;
    }
    for (final m in _yearRegex.allMatches(fileName)) {
      final y = int.tryParse(m.group(0)!);
      if (y != null && y >= 1970 && y <= 2100) return true;
    }
    return false;
  }

  /// 按全部启用的模式依次尝试（按权重降序），返回第一个解析成功且构造合法的日期。
  static DateTime? tryExtract(String fileName, List<PatternDefinition> patterns) {
    final ordered = [...patterns.where((p) => p.enabled)]
      ..sort((a, b) => b.weight.compareTo(a.weight));
    for (final pattern in ordered) {
      final d = tryExtractOne(fileName, pattern);
      if (d != null) return d;
    }
    return null;
  }

  static DateTime? tryExtractOne(String fileName, PatternDefinition pattern) {
    if (!pattern.enabled || pattern.pattern.isEmpty) return null;

    final regex = _getCachedRegex(pattern.pattern);
    if (regex == null) return null;

    final m = regex.firstMatch(fileName);
    if (m == null) return null;

    final mapping = pattern.groupMapping;
    final ignored = pattern.ignoredGroups;

    // 时间戳类型
    final tsGroup = mapping['timestamp'];
    if (tsGroup != null && !ignored.contains(tsGroup) && m.group(tsGroup) != null) {
      var digits = m.group(tsGroup)!;
      final len = pattern.timestampLength;
      if (len != null && digits.length > len) digits = digits.substring(0, len);
      return timestampToDate(digits, pattern.timestampLength);
    }

    // 年月日时分秒类型
    int group(String key, [int defaultValue = 0]) {
      final idx = mapping[key];
      if (idx == null || ignored.contains(idx)) return defaultValue;
      final g = m.group(idx);
      final v = g == null ? null : int.tryParse(g);
      return v ?? defaultValue;
    }

    final year = group('year', -1);
    if (year < 1) return null;
    final month = group('month', 1);
    final day = group('day', 1);
    final hour = group('hour');
    final minute = group('minute');
    final second = group('second');

    try {
      // 非法日期（如 2024-02-31）由 DateTime 构造抛出，视为该模式无结果
      return DateTime(year, month, day, hour, minute, second);
    } catch (_) {
      return null;
    }
  }

  static DateTime? timestampToDate(String digits, int? length) {
    final ts = int.tryParse(digits);
    if (ts == null) return null;
    try {
      switch (length) {
        case 13:
          return DateTime.fromMillisecondsSinceEpoch(ts).toLocal();
        case 16:
          return DateTime.fromMillisecondsSinceEpoch(ts ~/ 1000).toLocal(); // 微秒×10
        default:
          return DateTime.fromMillisecondsSinceEpoch(ts * 1000).toLocal(); // 10 位秒级
      }
    } on RangeError {
      return null; // 超出平台时间范围
    }
  }
}

/// 结构指纹：文件名字符分类为 D(数字)/L(字母)/S(符号)，用于排序聚类（SRS FR-3.4/FR-A6.1）。
String structureFingerprint(String fileName) {
  final sb = StringBuffer();
  for (final c in fileName.runes) {
    if (c >= 0x30 && c <= 0x39) {
      sb.writeCharCode(0x44); // D
    } else if ((c >= 0x41 && c <= 0x5A) || (c >= 0x61 && c <= 0x7A)) {
      sb.writeCharCode(0x4C); // L
    } else {
      sb.writeCharCode(0x53); // S
    }
  }
  return sb.toString();
}
