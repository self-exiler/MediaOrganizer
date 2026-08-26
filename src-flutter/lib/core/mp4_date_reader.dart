/// MP4/MOV 容器拍摄日期读取（替代 C# 版的 TagLib#）。
///
/// 解析 `moov → mvhd` 盒子的 creation_time（v0 32 位 / v1 64 位，自 1904-01-01 UTC 起的秒数）。
/// 仅需顺序跳过顶层与 moov 内盒子头，随机访问开销极小。AVI/MKV 等其他容器不支持，
/// 返回 null 后由文件名提取器兜底（对应 SRS R-A1 缓解策略）。
library;

import 'dart:io';

class Mp4DateReader {
  /// 1904-01-01 UTC：MP4 时间戳纪元。
  static final DateTime _epoch = DateTime.utc(1904);

  /// 读失败/无 moov/mvhd 时返回 null。
  static DateTime? readCreationTime(String path) {
    final raf = File(path).openSync();
    try {
      final length = raf.lengthSync();
      final moov = _findBox(raf, 0, length, 'moov');
      if (moov == null) return null;
      final mvhd =
          _findBox(raf, moov.$1, moov.$1 + moov.$2 - 8, 'mvhd');
      if (mvhd == null) return null;
      return _parseMvhd(raf, mvhd.$1);
    } catch (_) {
      return null;
    } finally {
      raf.closeSync();
    }
  }

  /// 在 [start, end) 偏移范围内查找指定类型的直接子盒子，
  /// 返回其载荷起始偏移与载荷大小。
  static (int, int)? _findBox(
      RandomAccessFile raf, int start, int end, String type) {
    final length = raf.lengthSync();
    var offset = start.clamp(0, length);
    final safeEnd = end.clamp(0, length);
    while (offset + 8 <= safeEnd) {
      raf.setPositionSync(offset);
      final header = raf.readSync(8);
      if (header.length < 8) return null;
      var size = _u32(header, 0);
      final boxType = String.fromCharCodes(header.sublist(4, 8));
      var payloadStart = offset + 8;
      var boxSize = size;
      if (size == 1) {
        // largesize（64 位）
        final large = raf.readSync(8);
        if (large.length < 8) return null;
        boxSize = (_u32(large, 0) << 32) | _u32(large, 4);
        payloadStart += 8;
      } else if (size == 0) {
        // 盒子延伸到文件末尾
        boxSize = length - offset;
      }
      if (boxSize <= 0) return null;
      if (boxType == type) {
        return (payloadStart, boxSize - (payloadStart - offset));
      }
      offset += boxSize;
    }
    return null;
  }

  static DateTime? _parseMvhd(RandomAccessFile raf, int payloadStart) {
    raf.setPositionSync(payloadStart);
    final head = raf.readSync(5); // version(1) + flags(3)，随后是时间字段
    if (head.length < 5) return null;
    final version = head[0];
    if (version == 1) {
      final fields = raf.readSync(20); // creation u64 + modification u64 + timescale u32
      if (fields.length < 20) return null;
      final creation = (_u32(fields, 0) << 32) | _u32(fields, 4);
      return _fromSeconds(creation);
    }
    final fields = raf.readSync(12); // creation u32 + modification u32 + timescale u32
    if (fields.length < 12) return null;
    return _fromSeconds(_u32(fields, 0));
  }

  static DateTime? _fromSeconds(int seconds) {
    if (seconds <= 0) return null;
    try {
      final dt = _epoch.add(Duration(seconds: seconds)).toLocal();
      if (dt.year < 1970 || dt.year > 2100) return null;
      return dt;
    } on RangeError {
      return null;
    }
  }

  static int _u32(List<int> b, int i) =>
      (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
}
