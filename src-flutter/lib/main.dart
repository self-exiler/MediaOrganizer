/// MediaOrganizer Flutter 版组合根：
/// 初始化数据目录 → 加载 AppState（config.json / patterns.json）→ 构建 MainViewModel → runApp。
library;

import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:path_provider/path_provider.dart';

import 'core/app_state.dart';
import 'ui/screens/home_shell.dart';
import 'ui/theme.dart';
import 'viewmodels/main_view_model.dart';
import 'viewmodels/settings_view_model.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // 应用专有目录（Android: /data/data/.../app_flutter；桌面：Documents）
  String dataDir;
  try {
    dataDir = (await getApplicationDocumentsDirectory()).path;
  } catch (_) {
    dataDir = Directory.systemTemp.path; // 插件不可用时的兜底
  }

  final state = AppState.load(
    '$dataDir/config.json',
    '$dataDir/patterns.json',
  );
  final vm = MainViewModel(state: state, dataDir: dataDir);

  // 设置页主题切换即时生效
  vm.settings.onThemeChanged = (_) => vm.notifyListeners();

  runApp(MediaOrganizerApp(vm: vm));
}

class MediaOrganizerApp extends StatelessWidget {
  final MainViewModel vm;

  const MediaOrganizerApp({super.key, required this.vm});

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: Listenable.merge([vm, vm.settings]),
      builder: (context, _) {
        return MaterialApp(
          title: 'MediaOrganizer',
          debugShowCheckedModeBanner: false,
          theme: buildLightTheme(),
          darkTheme: buildDarkTheme(),
          themeMode:
              themeModeFromName(vm.state.config.general.theme),
          localizationsDelegates: const [
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          supportedLocales: const [Locale('zh', 'CN'), Locale('en')],
          locale: const Locale('zh', 'CN'),
          home: HomeShell(vm: vm),
        );
      },
    );
  }
}
