/// 存储工厂（对应 Core/StorageFactory.cs）：按配置解析输出目标存储。
/// 本地目录全量支持；WebDAV 走 dart:io HttpClient；SMB 未实现（明确报错）。
library;

import 'dart:io';

import '../core/app_config.dart';
import '../core/enums.dart';
import 'file_storage.dart';
import 'webdav_storage.dart';

class StorageFactory {
  /// 按配置创建输出目标：OutputNetworkProfile 为空 → 本地 OutputDir；
  /// 否则查找同名 NetworkProfile（Smb / WebDav）。
  static FileStorage createTarget(AppConfig config) {
    final profileName = config.paths.outputNetworkProfile;
    if (profileName.isEmpty) {
      return LocalFileStorage(config.paths.outputDir);
    }
    final profile = config.networkProfiles
        .where((p) => p.name == profileName)
        .firstOrNull;
    if (profile == null) {
      // 配置被删但工作台仍指向它：退回本地目标
      return LocalFileStorage(config.paths.outputDir);
    }
    switch (profile.type) {
      case NetworkType.webDav:
        return WebDavStorage(
          baseUrl: profile.address,
          username: profile.username,
          password: profile.password,
        );
      case NetworkType.smb:
        throw UnsupportedError(
            'Flutter 版暂不支持 SMB 输出目标（可改用 WebDAV，或在桌面版执行 SMB 归档）');
    }
  }

  /// 创建本地存储（失败文件待处理目录用）。
  static LocalFileStorage createLocalStorage(String dir) =>
      LocalFileStorage(dir);

  /// 测试连接（FR-A9.3）：返回 (是否成功, 提示消息)。
  static Future<(bool, String)> testConnection(NetworkProfile profile) async {
    if (profile.address.trim().isEmpty) {
      return (false, '地址为空');
    }
    switch (profile.type) {
      case NetworkType.webDav:
        return WebDavStorage.testConnection(NetworkProfileLike(
          profile.address,
          profile.username,
          profile.password,
        ));
      case NetworkType.smb:
        return (false, 'Flutter 版暂不支持 SMB 直连，请使用 WebDAV 目标或桌面版');
    }
  }
}

extension _FirstOrNull<T> on Iterable<T> {
  T? get firstOrNull => isEmpty ? null : first;
}
