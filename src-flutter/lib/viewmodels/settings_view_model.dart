/// 设置 VM（对应 Shared/ViewModels/SettingsViewModel.cs）：
/// 提取器 / 文件名模式 / 网络位置 / 扫描参数。所有改动即时写回并落盘，
/// 同时经 AppState.notifyChanged 通知工作台重建提取链。
library;

import 'package:flutter/foundation.dart';

import '../core/app_config.dart';
import '../core/app_state.dart';
import '../core/enums.dart';
import '../core/patterns_store.dart';
import '../services/credential_crypto.dart';
import '../services/storage_factory.dart';

/// 提取器设置项。
class ExtractorSettingVM extends ChangeNotifier {
  final String name;
  final AppState state;

  bool enabled;
  double weight; // 0.0 ~ 2.0（UI 滑块 0~20 / 10）

  ExtractorSettingVM({required this.state, required this.name, required this.enabled, required this.weight});

  /// 展示名（移动端设置页用）。
  String get displayLabel => switch (name) {
        'Exif' => 'EXIF 提取器',
        'FileName' => '文件名提取器',
        'FileSystem' => '文件系统提取器',
        _ => name,
      };

  void setEnabled(bool v) {
    enabled = v;
    _writeThrough('启用状态');
    notifyListeners();
  }

  void setWeight(double v) {
    weight = v;
    notifyListeners();
  }

  /// 滑块拖动结束才落盘，避免高频写 config.json。
  void commitWeight() => _writeThrough('权重');

  void _writeThrough(String what) {
    final setting = state.config.extraction.setting(name);
    if (setting == null) return;
    setting.enabled = enabled;
    setting.weight = weight;
    state.saveConfig(notifyChanged: false);
    state.notifyChanged(); // 触发工作台链重建提示
  }
}

/// 文件名模式列表项。
class PatternSettingVM {
  final PatternDefinition source;

  PatternSettingVM(this.source);

  String get name => source.name;
  String get regex => source.pattern;
  String get sourceLabel => source.builtin ? '内置' : '自定义';

  bool get enabled => source.enabled;

  void setEnabled(bool v, {required AppState state}) {
    source.enabled = v;
    state.savePatterns();
  }
}

/// 网络位置列表项。
class NetworkProfileVM {
  final NetworkProfile source;

  NetworkProfileVM(this.source);

  String get name => source.name;
  String get typeLabel => source.type == NetworkType.smb ? 'SMB' : 'WebDAV';
  String get address => source.address;
  String get verifiedLabel => source.lastVerifiedAt == null
      ? '未验证'
      : '✓ ${_two(source.lastVerifiedAt!.year)}-${_two(source.lastVerifiedAt!.month)}-${_two(source.lastVerifiedAt!.day)} ${_two(source.lastVerifiedAt!.hour)}:${_two(source.lastVerifiedAt!.minute)}';

  static String _two(int v) => v.toString().padLeft(2, '0');
}

class SettingsViewModel extends ChangeNotifier {
  final AppState state;

  /// 主题变更回调（外壳切换 ThemeMode）。
  void Function(String theme)? onThemeChanged;

  SettingsViewModel({required this.state});

  // ---- 提取器 ----
  List<ExtractorSettingVM> buildExtractors() => [
        for (final s in state.config.extraction.extractors)
          ExtractorSettingVM(
            state: state,
            name: s.name,
            enabled: s.enabled,
            weight: s.weight,
          ),
      ];

  int get maxYearsPast => state.config.extraction.maxYearsPast;
  static const maxYearsPastOptions = [20, 30, 50];

  void setMaxYearsPast(int years) {
    state.config.extraction.maxYearsPast = years;
    state.saveConfig();
    notifyListeners();
  }

  int get futureDateBufferDays => state.config.extraction.futureDateBufferDays;
  static const futureBufferOptions = [0, 1, 7];

  void setFutureBufferDays(int days) {
    state.config.extraction.futureDateBufferDays = days;
    state.saveConfig();
    notifyListeners();
  }

  // ---- 文件名模式 ----
  List<PatternSettingVM> get patterns =>
      [for (final p in state.patterns) PatternSettingVM(p)];

  String get patternSummary {
    final total = state.patterns.length;
    final enabled = state.patterns.where((p) => p.enabled).length;
    return '$total 条 · $enabled 启用';
  }

  void setPatternEnabled(PatternSettingVM vm, bool v) {
    vm.setEnabled(v, state: state);
    notifyListeners();
  }

  void deletePattern(PatternSettingVM vm) {
    state.patterns.removeWhere((p) => p.name == vm.name);
    state.savePatterns();
    notifyListeners();
  }

  /// 内置模式回到出厂清单（保留自定义模式）。
  void restoreDefaultPatterns() {
    for (final builtin in PatternsStore.getBuiltinPatterns()) {
      if (!state.patterns.any((p) => p.name == builtin.name)) {
        state.patterns.add(builtin);
      }
    }
    state.savePatterns();
    notifyListeners();
  }

  // ---- 网络位置 ----
  List<NetworkProfileVM> get networkProfiles =>
      [for (final p in state.config.networkProfiles) NetworkProfileVM(p)];

  /// 保存（新增或按名称替换）。返回 null 表示成功，否则为错误提示。
  String? saveNetwork({
    required String originalName, // 空 = 新建
    required String name,
    required NetworkType type,
    required String address,
    required String username,
    required String password, // 明文；空表示编辑时保留原密码
  }) {
    if (name.trim().isEmpty) return '请填写名称';
    if (address.trim().isEmpty) return r'请填写地址（SMB: \\server\share；WebDAV: https://…）';
    final profiles = state.config.networkProfiles;

    final existing = profiles.where((p) => p.name == name.trim()).firstOrNull;
    if (originalName.isEmpty && existing != null) {
      return '名称「${name.trim()}」已存在';
    }

    if (originalName.isEmpty) {
      profiles.add(NetworkProfile(
        name: name.trim(),
        type: type,
        address: address.trim(),
        username: username.trim(),
        password: password.isEmpty ? '' : CredentialCrypto.encrypt(password),
      ));
    } else {
      final target = profiles.where((p) => p.name == originalName).firstOrNull;
      if (target == null) return '原配置不存在';
      target
        ..name = name.trim()
        ..type = type
        ..address = address.trim()
        ..username = username.trim()
        ..password = password.isEmpty
            ? target.password
            : CredentialCrypto.encrypt(password);
      // 工作台引用的名称同步更新
      if (state.config.paths.outputNetworkProfile == originalName) {
        state.config.paths.outputNetworkProfile = target.name;
      }
    }

    state.saveConfig(notifyChanged: false);
    state.notifyChanged();
    return null;
  }

  void deleteNetwork(String name) {
    state.config.networkProfiles.removeWhere((p) => p.name == name);
    if (state.config.paths.outputNetworkProfile == name) {
      state.config.paths.outputNetworkProfile = '';
    }
    state.saveConfig(notifyChanged: false);
    state.notifyChanged();
    notifyListeners();
  }

  Future<(bool, String)> testNetwork(NetworkProfile profile) async {
    final (ok, message) = await StorageFactory.testConnection(profile);
    if (ok) {
      profile.lastVerifiedAt = DateTime.now();
      state.saveConfig(notifyChanged: false);
      notifyListeners();
    }
    return (ok, message);
  }

  // ---- 扫描参数 ----
  bool get scanAllFiles => state.config.scan.scanAllFiles;

  void setScanAllFiles(bool v) {
    state.config.scan.scanAllFiles = v;
    state.saveConfig();
    notifyListeners();
  }

  String get supportedFormatsText => state.config.scan.supportedFormats.join(', ');

  void setSupportedFormatsText(String value) {
    state.config.scan.supportedFormats = value
        .split(RegExp(r'[,，;；\s]+'))
        .map((s) => s.trim().replaceAll('.', ''))
        .where((s) => s.isNotEmpty)
        .toList();
    state.saveConfig();
    notifyListeners();
  }

  int get executionParallelism => state.config.execute.maxDegreeOfParallelism;

  void setExecutionParallelism(int v) {
    state.config.execute.maxDegreeOfParallelism = v;
    state.saveConfig(notifyChanged: false);
    notifyListeners();
  }

  // ---- 主题 ----
  int get themeIndex => switch (state.config.general.theme) {
        'Light' => 1,
        'Dark' => 2,
        _ => 0,
      };

  void setThemeIndex(int index) {
    state.config.general.theme = switch (index) {
      1 => 'Light',
      2 => 'Dark',
      _ => 'Default',
    };
    onThemeChanged?.call(state.config.general.theme);
    state.saveConfig(notifyChanged: false);
    notifyListeners();
  }

  // ---- 恢复默认 ----
  String initialize() {
    state.config.resetToDefaults();
    for (final builtin in PatternsStore.getBuiltinPatterns()) {
      if (!state.patterns.any((p) => p.name == builtin.name)) {
        state.patterns.add(builtin);
      }
    }
    state.saveAll();
    notifyListeners();
    return '已恢复默认配置与内置文件名模式';
  }
}

extension _FirstOrNull<T> on Iterable<T> {
  T? get firstOrNull => isEmpty ? null : first;
}
