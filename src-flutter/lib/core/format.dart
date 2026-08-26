/// 数字/百分比格式化帮助（避免引入 intl 依赖）。
library;

/// 千分位分组：1234567 → "1,234,567"。
String formatN0(num v) {
  final s = v.toInt().toString();
  final negative = s.startsWith('-');
  final digits = negative ? s.substring(1) : s;
  final b = StringBuffer(negative ? '-' : '');
  for (var i = 0; i < digits.length; i++) {
    b.write(digits[i]);
    final rem = digits.length - i - 1;
    if (rem > 0 && rem % 3 == 0) b.write(',');
  }
  return b.toString();
}

/// 百分比一位小数：0.9823 → "98.2%"。
String formatP1(double v) => '${(v * 100).toStringAsFixed(1)}%';

/// 文件大小人性化：2.1MB / 512KB。
String formatSize(int bytes) {
  if (bytes >= 1024 * 1024 * 1024) {
    return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(1)}GB';
  }
  if (bytes >= 1024 * 1024) {
    return '${(bytes / (1024 * 1024)).toStringAsFixed(1)}MB';
  }
  if (bytes >= 1024) return '${(bytes / 1024).toStringAsFixed(0)}KB';
  return '$bytes B';
}
