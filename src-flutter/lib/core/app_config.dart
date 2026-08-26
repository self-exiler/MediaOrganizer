/// 应用配置（config.json）。结构与键名对应 Core/Configuration/AppConfig.cs，
/// 保持 PascalCase 键以便与桌面版生成的配置互认。
library;

import 'dart:convert';
import 'dart:io';

import 'enums.dart';

/// JSON 原子读写（建目录 + 序列化 + 落盘），供配置/模式/分析结果共用。
class JsonFileStore {
  static void save(String path, Object payload) {
    final f = File(path);
    f.parent.createSync(recursive: true);
    const encoder = JsonEncoder.withIndent('  ');
    f.writeAsStringSync(encoder.convert(payload));
  }

  /// 文件缺失/损坏返回 null（与 C# JsonFileStore.Load 一致，调用方回退默认值）。
  static Map<String, Object?>? load(String path) {
    try {
      final f = File(path);
      if (!f.existsSync()) return null;
      final decoded = jsonDecode(f.readAsStringSync());
      return decoded is Map<String, Object?> ? decoded : null;
    } catch (_) {
      return null;
    }
  }
}

/// 网络位置连接配置（FR-10/FR-A8.3）。
class NetworkProfile {
  String name;
  NetworkType type;
  /// SMB：UNC 路径 \\server\share；WebDAV：完整地址 https://dav.example.com/photos。
  String address;
  String username;
  /// 加密存储（B64: 前缀，见 services/credential_crypto.dart）。
  String password;
  DateTime? lastVerifiedAt;

  NetworkProfile({
    this.name = '',
    this.type = NetworkType.smb,
    this.address = '',
    this.username = '',
    this.password = '',
    this.lastVerifiedAt,
  });

  Map<String, Object?> toJson() => {
        'Name': name,
        'Type': networkTypeName(type),
        'Address': address,
        'Username': username,
        'Password': password,
        'LastVerifiedAt': lastVerifiedAt?.toIso8601String(),
      };

  factory NetworkProfile.fromJson(Map<String, Object?> j) => NetworkProfile(
        name: (j['Name'] as String?) ?? '',
        type: networkTypeFromName(j['Type'] as String?),
        address: (j['Address'] as String?) ?? '',
        username: (j['Username'] as String?) ?? '',
        password: (j['Password'] as String?) ?? '',
        lastVerifiedAt: j['LastVerifiedAt'] == null
            ? null
            : DateTime.tryParse(j['LastVerifiedAt'] as String),
      );

  NetworkProfile copy() => NetworkProfile(
        name: name,
        type: type,
        address: address,
        username: username,
        password: password,
        lastVerifiedAt: lastVerifiedAt,
      );
}

/// 单个提取器的启用与权重配置。
class ExtractorSetting {
  String name;
  bool enabled;
  double weight;

  ExtractorSetting({this.name = '', this.enabled = true, this.weight = 1.0});

  Map<String, Object?> toJson() =>
      {'Name': name, 'Enabled': enabled, 'Weight': weight};

  factory ExtractorSetting.fromJson(Map<String, Object?> j) => ExtractorSetting(
        name: (j['Name'] as String?) ?? '',
        enabled: (j['Enabled'] as bool?) ?? true,
        weight: (j['Weight'] as num?)?.toDouble() ?? 1.0,
      );

  ExtractorSetting copy() => ExtractorSetting(name: name, enabled: enabled, weight: weight);
}

class GeneralConfig {
  String theme; // Default / Light / Dark
  String windowSize;
  int previewSize;

  GeneralConfig({this.theme = 'Default', this.windowSize = '1280x760', this.previewSize = 320});

  Map<String, Object?> toJson() =>
      {'Theme': theme, 'WindowSize': windowSize, 'PreviewSize': previewSize};

  factory GeneralConfig.fromJson(Map<String, Object?>? j) => j == null
      ? GeneralConfig()
      : GeneralConfig(
          theme: (j['Theme'] as String?) ?? 'Default',
          windowSize: (j['WindowSize'] as String?) ?? '1280x760',
          previewSize: (j['PreviewSize'] as num?)?.toInt() ?? 320,
        );
}

class PathsConfig {
  String sourceDir;
  String outputDir;

  /// 输出目标为网络位置时，此处存 NetworkProfile.Name（空 = 本地）。
  String outputNetworkProfile;
  String pendingDir;

  PathsConfig({
    this.sourceDir = '',
    this.outputDir = '',
    this.outputNetworkProfile = '',
    this.pendingDir = '',
  });

  Map<String, Object?> toJson() => {
        'SourceDir': sourceDir,
        'OutputDir': outputDir,
        'OutputNetworkProfile': outputNetworkProfile,
        'PendingDir': pendingDir,
      };

  factory PathsConfig.fromJson(Map<String, Object?>? j) => j == null
      ? PathsConfig()
      : PathsConfig(
          sourceDir: (j['SourceDir'] as String?) ?? '',
          outputDir: (j['OutputDir'] as String?) ?? '',
          outputNetworkProfile: (j['OutputNetworkProfile'] as String?) ?? '',
          pendingDir: (j['PendingDir'] as String?) ?? '',
        );
}

class ScanConfig {
  List<String> supportedFormats;
  /// 扫描所有文件（忽略扩展名白名单）。FR-1.3/FR-A1.3。
  bool scanAllFiles;
  int progressInterval;
  /// 0 = 自动。Dart 版分析在单 Isolate 内顺序执行，此值保留兼容配置文件。
  int maxDegreeOfParallelism;

  ScanConfig({
    List<String>? supportedFormats,
    this.scanAllFiles = false,
    this.progressInterval = 10,
    this.maxDegreeOfParallelism = 0,
  }) : supportedFormats = supportedFormats ??
            [
              'jpg', 'jpeg', 'png', 'tiff', 'tif', 'bmp', 'webp', 'heic', 'heif',
              'mp4', 'mov', 'avi', 'mkv', 'wmv', 'flv', 'webm', 'm4v', 'mpg',
              'mpeg', '3gp', '3g2',
            ];

  Map<String, Object?> toJson() => {
        'SupportedFormats': supportedFormats,
        'ScanAllFiles': scanAllFiles,
        'ProgressInterval': progressInterval,
        'MaxDegreeOfParallelism': maxDegreeOfParallelism,
      };

  factory ScanConfig.fromJson(Map<String, Object?>? j) => j == null
      ? ScanConfig()
      : ScanConfig(
          supportedFormats:
              ((j['SupportedFormats'] as List<Object?>?) ?? const [])
                  .whereType<String>()
                  .toList(),
          scanAllFiles: (j['ScanAllFiles'] as bool?) ?? false,
          progressInterval: (j['ProgressInterval'] as num?)?.toInt() ?? 10,
          maxDegreeOfParallelism:
              (j['MaxDegreeOfParallelism'] as num?)?.toInt() ?? 0,
        );
}

class ExtractionConfig {
  int maxYearsPast;
  int futureDateBufferDays;
  List<ExtractorSetting> extractors;

  ExtractionConfig({
    this.maxYearsPast = 30,
    this.futureDateBufferDays = 0,
    List<ExtractorSetting>? extractors,
  }) : extractors = extractors ?? [
          ExtractorSetting(name: 'Exif', weight: 1.2),
          ExtractorSetting(name: 'FileName', weight: 1.1),
          ExtractorSetting(name: 'FileSystem', enabled: false, weight: 0.8),
        ];

  Map<String, Object?> toJson() => {
        'MaxYearsPast': maxYearsPast,
        'FutureDateBufferDays': futureDateBufferDays,
        'Extractors': extractors.map((e) => e.toJson()).toList(),
      };

  factory ExtractionConfig.fromJson(Map<String, Object?>? j) => j == null
      ? ExtractionConfig()
      : ExtractionConfig(
          maxYearsPast: (j['MaxYearsPast'] as num?)?.toInt() ?? 30,
          futureDateBufferDays:
              (j['FutureDateBufferDays'] as num?)?.toInt() ?? 0,
          extractors: ((j['Extractors'] as List<Object?>?) ?? const [])
              .whereType<Map<String, Object?>>()
              .map(ExtractorSetting.fromJson)
              .toList(),
        );

  ExtractorSetting? setting(String name) {
    for (final e in extractors) {
      if (e.name == name) return e;
    }
    return null;
  }
}

class ExecuteConfig {
  FileOperation operation;
  ExistAction existAction;
  ClassificationLevel classificationLevel;
  bool fixMtime;
  /// 文件执行并行度：0 = 自动（本地 2 / 网络 4）。
  int maxDegreeOfParallelism;

  ExecuteConfig({
    this.operation = FileOperation.copy,
    this.existAction = ExistAction.skip,
    this.classificationLevel = ClassificationLevel.day,
    this.fixMtime = false,
    this.maxDegreeOfParallelism = 0,
  });

  Map<String, Object?> toJson() => {
        'Operation': operation == FileOperation.move ? 'Move' : 'Copy',
        'ExistAction': switch (existAction) {
          ExistAction.overwrite => 'Overwrite',
          ExistAction.rename => 'Rename',
          ExistAction.skip => 'Skip',
        },
        'ClassificationLevel': switch (classificationLevel) {
          ClassificationLevel.year => 'Year',
          ClassificationLevel.month => 'Month',
          ClassificationLevel.day => 'Day',
        },
        'FixMtime': fixMtime,
        'MaxDegreeOfParallelism': maxDegreeOfParallelism,
      };

  factory ExecuteConfig.fromJson(Map<String, Object?>? j) => j == null
      ? ExecuteConfig()
      : ExecuteConfig(
          operation: fileOperationFromName(j['Operation'] as String?),
          existAction: existActionFromName(j['ExistAction'] as String?),
          classificationLevel:
              classificationLevelFromName(j['ClassificationLevel'] as String?),
          fixMtime: (j['FixMtime'] as bool?) ?? false,
          maxDegreeOfParallelism:
              (j['MaxDegreeOfParallelism'] as num?)?.toInt() ?? 0,
        );
}

/// 应用配置根对象（config.json）。
class AppConfig {
  int version;
  GeneralConfig general;
  PathsConfig paths;
  ScanConfig scan;
  ExtractionConfig extraction;
  ExecuteConfig execute;
  List<NetworkProfile> networkProfiles;

  AppConfig({
    this.version = 1,
    GeneralConfig? general,
    PathsConfig? paths,
    ScanConfig? scan,
    ExtractionConfig? extraction,
    ExecuteConfig? execute,
    List<NetworkProfile>? networkProfiles,
  })  : general = general ?? GeneralConfig(),
        paths = paths ?? PathsConfig(),
        scan = scan ?? ScanConfig(),
        extraction = extraction ?? ExtractionConfig(),
        execute = execute ?? ExecuteConfig(),
        networkProfiles = networkProfiles ?? [];

  Map<String, Object?> toJson() => {
        'Version': version,
        'General': general.toJson(),
        'Paths': paths.toJson(),
        'Scan': scan.toJson(),
        'Extraction': extraction.toJson(),
        'Execute': execute.toJson(),
        'NetworkProfiles': networkProfiles.map((p) => p.toJson()).toList(),
      };

  factory AppConfig.fromJson(Map<String, Object?> j) => AppConfig(
        version: (j['Version'] as num?)?.toInt() ?? 1,
        general: GeneralConfig.fromJson(_map(j['General'])),
        paths: PathsConfig.fromJson(_map(j['Paths'])),
        scan: ScanConfig.fromJson(_map(j['Scan'])),
        extraction: ExtractionConfig.fromJson(_map(j['Extraction'])),
        execute: ExecuteConfig.fromJson(_map(j['Execute'])),
        networkProfiles: ((j['NetworkProfiles'] as List<Object?>?) ?? const [])
            .whereType<Map<String, Object?>>()
            .map(NetworkProfile.fromJson)
            .toList(),
      );

  static Map<String, Object?>? _map(Object? v) =>
      v is Map<String, Object?> ? v : null;

  /// 恢复默认（网络位置不随初始化清除，避免误删用户配置——同 C# ResetConfigToDefaults）。
  void resetToDefaults() {
    final d = AppConfig();
    version = d.version;
    general = d.general;
    paths = d.paths;
    scan = d.scan;
    extraction = d.extraction;
    execute = d.execute;
  }
}
