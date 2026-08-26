/// 应用状态单一所有者（对应 Core/AppState.cs）：
/// 持有 config + patterns + 各自路径，唯一保存入口与变更通知。
library;

import 'app_config.dart';
import 'patterns_store.dart';

class AppState {
  final AppConfig config;
  final List<PatternDefinition> patterns;
  final String configPath;
  final String patternsPath;

  AppState({
    required this.config,
    required this.patterns,
    required this.configPath,
    required this.patternsPath,
  });

  /// 任意配置/模式变更后触发（工作台据此重建提取链并失效旧计划）。
  final _changedListeners = <void Function()>[];

  void addListener(void Function() listener) => _changedListeners.add(listener);

  void removeListener(void Function() listener) =>
      _changedListeners.remove(listener);

  void notifyChanged() {
    for (final listener in List.of(_changedListeners)) {
      listener();
    }
  }

  static AppState load(String configPath, String patternsPath) => AppState(
        config: JsonFileStore.load(configPath)?.let(AppConfig.fromJson) ??
            AppConfig(),
        patterns: PatternsStore.load(patternsPath),
        configPath: configPath,
        patternsPath: patternsPath,
      );

  /// 保存配置。默认触发 Changed；仅持久化不影响提取链的字段时传 notifyChanged: false。
  void saveConfig({bool notifyChanged = true}) {
    JsonFileStore.save(configPath, config.toJson());
    if (notifyChanged) notifyChanged();
  }

  void savePatterns() {
    PatternsStore.save(patternsPath, patterns);
    notifyChanged();
  }

  /// 保存配置与模式并通知变更。
  void saveAll() {
    JsonFileStore.save(configPath, config.toJson());
    PatternsStore.save(patternsPath, patterns);
    notifyChanged();
  }
}

extension _Let<T> on T {
  R let<R>(R Function(T) f) => f(this);
}
