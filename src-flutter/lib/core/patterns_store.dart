/// 文件名日期解析模式（patterns.json），结构对应 Core/Configuration/PatternsStore.cs。
/// 键名 PascalCase 与桌面版一致：桌面版魔术工具编辑的 patterns.json 可直接复制到本应用。
library;

import 'app_config.dart' show JsonFileStore;

/// 一条文件名日期解析模式。
class PatternDefinition {
  final String name;
  final String pattern;

  /// 捕获组名 → 组号。支持键：year/month/day/hour/minute/second/timestamp。
  final Map<String, int> groupMapping;

  /// 明确忽略的捕获组号（如前后缀 (.*) 组），解析时跳过。
  final List<int> ignoredGroups;

  /// 时间戳位数：10（秒）/ 13（毫秒）/ 16（微秒×10）。
  final int? timestampLength;
  bool enabled;
  double weight;
  final bool builtin;

  PatternDefinition({
    required this.name,
    required this.pattern,
    Map<String, int>? groupMapping,
    List<int>? ignoredGroups,
    this.timestampLength,
    this.enabled = true,
    this.weight = 1.0,
    this.builtin = false,
  })  : groupMapping = groupMapping ?? {},
        ignoredGroups = ignoredGroups ?? [];

  Map<String, Object?> toJson() => {
        'Name': name,
        'Pattern': pattern,
        'GroupMapping': groupMapping,
        'IgnoredGroups': ignoredGroups,
        if (timestampLength != null) 'TimestampLength': timestampLength,
        'Enabled': enabled,
        'Weight': weight,
        'Builtin': builtin,
      };

  factory PatternDefinition.fromJson(Map<String, Object?> j) => PatternDefinition(
        name: (j['Name'] as String?) ?? '',
        pattern: (j['Pattern'] as String?) ?? '',
        groupMapping: ((j['GroupMapping'] as Map<Object?, Object?>?) ?? const {})
            .map((k, v) => MapEntry(k.toString(), (v as num).toInt())),
        ignoredGroups: ((j['IgnoredGroups'] as List<Object?>?) ?? const [])
            .whereType<num>()
            .map((n) => n.toInt())
            .toList(),
        timestampLength: (j['TimestampLength'] as num?)?.toInt(),
        enabled: (j['Enabled'] as bool?) ?? true,
        weight: (j['Weight'] as num?)?.toDouble() ?? 1.0,
        builtin: (j['Builtin'] as bool?) ?? false,
      );

  PatternDefinition copyWith({bool? enabled}) => PatternDefinition(
        name: name,
        pattern: pattern,
        groupMapping: Map.of(groupMapping),
        ignoredGroups: List.of(ignoredGroups),
        timestampLength: timestampLength,
        enabled: enabled ?? this.enabled,
        weight: weight,
        builtin: builtin,
      );
}

/// patterns.json 的读写与内置模式清单。
class PatternsStore {
  static const _versionKey = 'Version';
  static const _patternsKey = 'Patterns';

  /// 文件缺失或无有效内容时回退内置模式（与 C# PatternsStore.Load 一致）。
  static List<PatternDefinition> load(String path) {
    final file = JsonFileStore.load(path);
    if (file != null) {
      final list = file[_patternsKey];
      if (list is List<Object?> && list.isNotEmpty) {
        return list
            .whereType<Map<String, Object?>>()
            .map(PatternDefinition.fromJson)
            .toList();
      }
    }
    return getBuiltinPatterns();
  }

  static void save(String path, List<PatternDefinition> patterns) {
    JsonFileStore.save(path, {
      _versionKey: 1,
      _patternsKey: patterns.map((p) => p.toJson()).toList(),
    });
  }

  /// 内置模式：覆盖微信/小红书/OPPO/通用日期/紧凑日期/时间戳等常见场景。
  static List<PatternDefinition> getBuiltinPatterns() => [
        PatternDefinition(
          name: '微信导出',
          pattern: r'mm_export(\d{13})',
          groupMapping: {'timestamp': 1},
          timestampLength: 13,
          weight: 1.0,
          builtin: true,
        ),
        PatternDefinition(
          name: '小红书',
          pattern: r'(\d{4})(\d{2})(\d{2})[-_](\d{6})',
          groupMapping: {'year': 1, 'month': 2, 'day': 3, 'hour': 4, 'minute': 5, 'second': 6},
          weight: 0.9,
          builtin: true,
        ),
        PatternDefinition(
          name: '通用日期时间',
          pattern: r'(\d{4})[-_.](\d{1,2})[-_.](\d{1,2})[T _](\d{1,2})[-_.:](\d{2})[-_.:](\d{2})',
          groupMapping: {'year': 1, 'month': 2, 'day': 3, 'hour': 4, 'minute': 5, 'second': 6},
          weight: 1.0,
          builtin: true,
        ),
        PatternDefinition(
          name: '通用日期',
          pattern: r'(\d{4})[-_.](\d{1,2})[-_.](\d{1,2})',
          groupMapping: {'year': 1, 'month': 2, 'day': 3},
          weight: 0.9,
          builtin: true,
        ),
        PatternDefinition(
          name: '紧凑日期时间',
          pattern: r'(\d{4})(\d{2})(\d{2})[ _]?(\d{2})(\d{2})(\d{2})',
          groupMapping: {'year': 1, 'month': 2, 'day': 3, 'hour': 4, 'minute': 5, 'second': 6},
          weight: 0.9,
          builtin: true,
        ),
        PatternDefinition(
          name: '紧凑日期',
          pattern: r'(\d{4})(\d{2})(\d{2})',
          groupMapping: {'year': 1, 'month': 2, 'day': 3},
          weight: 0.8,
          builtin: true,
        ),
        PatternDefinition(
          name: '秒级时间戳',
          pattern: r'(^|[^0-9])(\d{10})([^0-9]|$)',
          groupMapping: {'timestamp': 2},
          timestampLength: 10,
          weight: 0.7,
          builtin: true,
        ),
        PatternDefinition(
          name: 'OPPO 相机',
          pattern: r'IMG_(\d{4})(\d{2})(\d{2})_(\d{6})',
          groupMapping: {'year': 1, 'month': 2, 'day': 3, 'hour': 4, 'minute': 5, 'second': 6},
          weight: 0.8,
          builtin: true,
        ),
        PatternDefinition(
          name: '毫秒级时间戳',
          pattern: r'(^|[^0-9])(\d{13})([^0-9]|$)',
          groupMapping: {'timestamp': 2},
          timestampLength: 13,
          weight: 0.7,
          builtin: true,
        ),
        PatternDefinition(
          name: '微秒级时间戳',
          pattern: r'(^|[^0-9])(\d{16})([^0-9]|$)',
          groupMapping: {'timestamp': 2},
          timestampLength: 16,
          weight: 0.6,
          builtin: true,
        ),
      ];
}
