/// 平台服务：系统目录选取 + 报告导出/分享/打开。
library;

import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:open_filex/open_filex.dart';
import 'package:share_plus/share_plus.dart';

/// 系统目录选择器（对应 IFolderPicker）。
class FolderPicker {
  /// 返回所选目录的真实路径；取消返回 null。
  static Future<String?> pick() =>
      FilePicker.platform.getDirectoryPath(dialogTitle: '选择文件夹');
}

/// 报告导出（对应 IFileSaver）。
class ReportExporter {
  /// 另存为（FR-A4.3 导出）。返回保存路径；取消返回 null。
  static Future<String?> saveTextAs(String fileName, String text) async {
    final bytes = Uint8List.fromList(utf8.encode(text));
    final path = await FilePicker.platform.saveFile(
      fileName: fileName,
      type: FileType.custom,
      allowedExtensions: const ['txt'],
      bytes: bytes, // Android/iOS 由插件直接写入
    );
    // 桌面端 saveFile 仅返回目标路径：兜底写盘
    if (path != null && !File(path).existsSync()) {
      File(path).writeAsStringSync(text);
    }
    return path;
  }

  /// 系统分享（Android 分享面板；桌面弹出分享窗口）。
  static Future<void> share(String title, String filePath) async {
    await Share.shareXFiles([XFile(filePath)], subject: title);
  }

  /// 用系统默认应用打开（FR-A6.3）。
  static Future<void> openWithSystem(String path) async {
    await OpenFilex.open(path);
  }
}
