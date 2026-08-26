/// TXT 分析报告生成（对应 Core/Analysis/AnalysisReportGenerator.cs，SRS FR-A4.3）。
/// 输出格式与桌面版逐行一致（含 █ 条形图）。
library;

import 'models.dart';

class AnalysisReportGenerator {
  static String generate(AnalysisResult result, String outputDir) {
    final sb = StringBuffer();
    final total = result.total;
    final parsedCount = result.parsed.length;

    String pct(num v) => '${(v * 100).toStringAsFixed(1)}%';
    String n0(int v) {
      final s = v.toString();
      final b = StringBuffer();
      for (var i = 0; i < s.length; i++) {
        final rem = s.length - i;
        b.write(s[i]);
        if (rem > 1 && rem % 3 == 1) b.write(',');
      }
      return b.toString();
    }

    sb.writeln('媒体文件分析报告');
    sb.writeln('==================');
    sb.writeln('源目录:    ${result.sourceDir}');
    sb.writeln('输出目录:  $outputDir');
    sb.writeln(
        '分析时间:  ${_fmt(result.analyzedAt)}');
    sb.writeln();
    sb.writeln(
        '总计: ${n0(total)}  成功: ${n0(parsedCount)} (${pct(result.successRate)})  失败: ${result.unparsed.length}');
    sb.writeln();

    // 按来源
    sb.writeln('按来源:');
    final bySource = <String, int>{};
    for (final p in result.parsed) {
      bySource[p.source] = (bySource[p.source] ?? 0) + 1;
    }
    final sources = bySource.keys.toList()
      ..sort((a, b) => bySource[b]!.compareTo(bySource[a]!));
    for (final source in sources) {
      final count = bySource[source]!;
      sb.writeln('  ${source.padRight(12)}${n0(count).padLeft(8)}  (${pct(total == 0 ? 0 : count / total)})');
    }
    sb.writeln();

    // 按年份
    sb.writeln('按年份:');
    _writeDistribution(sb, _groupBy(result.parsed, (p) => p.date.year.toString()),
        parsedCount);
    sb.writeln();

    // 按月分布
    sb.writeln('按月分布:');
    _writeDistribution(sb, _groupBy(result.parsed,
        (p) => '${p.date.year.toString().padLeft(4, '0')}-${p.date.month.toString().padLeft(2, '0')}'), parsedCount);
    sb.writeln();

    sb.writeln('失败文件（${result.unparsed.length}）:');
    for (final f in result.unparsed.take(200)) {
      sb.writeln('  ${f.file.fileName}   ${f.reason}');
    }
    if (result.unparsed.length > 200) {
      sb.writeln('  ... 其余 ${result.unparsed.length - 200} 个略');
    }
    return sb.toString();
  }

  static String _fmt(DateTime dt) =>
      '${dt.year.toString().padLeft(4, '0')}-${dt.month.toString().padLeft(2, '0')}-'
      '${dt.day.toString().padLeft(2, '0')} ${dt.hour.toString().padLeft(2, '0')}:'
      '${dt.minute.toString().padLeft(2, '0')}:${dt.second.toString().padLeft(2, '0')}';

  /// 按键提取函数分组并按 key 降序（对应 C# 的 GroupBy + OrderByDescending）。
  static Map<String, int> _groupBy(
      List<ParsedFile> files, String Function(ParsedFile) keyOf) {
    final map = <String, int>{};
    for (final p in files) {
      final k = keyOf(p);
      map[k] = (map[k] ?? 0) + 1;
    }
    final keys = map.keys.toList()..sort((a, b) => b.compareTo(a));
    return {for (final k in keys) k: map[k]!};
  }

  static void _writeDistribution(StringBuffer sb, Map<String, int> groups, int parsedCount) {
    for (final entry in groups.entries) {
      final barLen = (entry.value * 20 / (parsedCount == 0 ? 1 : parsedCount))
          .floor()
          .clamp(1, 20);
      sb.writeln('  ${entry.key}  ${'█' * barLen} ${entry.value.toString().padLeft(6)}');
    }
  }
}
