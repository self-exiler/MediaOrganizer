/// 原型色板与主题（对应 docs/android-界面原型设计/index.html 的 :root CSS 变量）。
library;

import 'package:flutter/material.dart';

class AppColors {
  final Color accent;
  final Color accentHover;
  final Color bg;
  final Color card;
  final Color cardBorder;
  final Color text;
  final Color text2;
  final Color text3;
  final Color success;
  final Color warning;
  final Color error;
  final Color rail;

  const AppColors({
    required this.accent,
    required this.accentHover,
    required this.bg,
    required this.card,
    required this.cardBorder,
    required this.text,
    required this.text2,
    required this.text3,
    required this.success,
    required this.warning,
    required this.error,
    required this.rail,
  });

  /// 浅色：原型默认值。
  static const light = AppColors(
    accent: Color(0xFF0067C0),
    accentHover: Color(0xFF1975C5),
    bg: Color(0xFFF5F5F5),
    card: Colors.white,
    cardBorder: Color(0xFFE5E5E5),
    text: Color(0xFF1B1B1B),
    text2: Color(0xFF616161),
    text3: Color(0xFF8A8A8A),
    success: Color(0xFF107C10),
    warning: Color(0xFFCA5010),
    error: Color(0xFFC42B1C),
    rail: Color(0xFFFAFAFA),
  );

  /// 深色：按原型语义映射的暗色变体。
  static const dark = AppColors(
    accent: Color(0xFF4FA3E3),
    accentHover: Color(0xFF6BB4EA),
    bg: Color(0xFF1E1E1E),
    card: Color(0xFF2B2B2B),
    cardBorder: Color(0xFF3D3D3D),
    text: Color(0xFFF0F0F0),
    text2: Color(0xFFB8B8B8),
    text3: Color(0xFF808080),
    success: Color(0xFF6CCB6C),
    warning: Color(0xFFF2994A),
    error: Color(0xFFE8695A),
    rail: Color(0xFF252525),
  );

  static AppColors of(BuildContext context) =>
      Theme.of(context).brightness == Brightness.dark ? dark : light;

  double get radius => 12;
}

ThemeData buildLightTheme() => _theme(Brightness.light);

ThemeData buildDarkTheme() => _theme(Brightness.dark);

ThemeData _theme(Brightness brightness) {
  final colors = brightness == Brightness.light ? AppColors.light : AppColors.dark;
  final scheme = ColorScheme.fromSeed(
    seedColor: AppColors.light.accent,
    brightness: brightness,
  ).copyWith(primary: colors.accent, error: colors.error);
  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    scaffoldBackgroundColor: colors.bg,
    appBarTheme: AppBarTheme(
      backgroundColor: colors.accent,
      foregroundColor: Colors.white,
      elevation: 0,
      toolbarHeight: 56,
      titleTextStyle: TextStyle(
        fontSize: 18,
        fontWeight: FontWeight.w500,
        color: Colors.white,
      ),
    ),
    dividerTheme: DividerThemeData(color: colors.cardBorder, thickness: 1),
    switchTheme: SwitchThemeData(
      thumbColor: WidgetStateProperty.resolveWith((states) =>
          states.contains(WidgetState.selected) ? Colors.white : null),
      trackColor: WidgetStateProperty.resolveWith((states) =>
          states.contains(WidgetState.selected) ? colors.accent : null),
    ),
    sliderTheme: SliderThemeData(
      activeTrackColor: colors.accent,
      inactiveTrackColor: colors.cardBorder,
      thumbColor: colors.accent,
    ),
    progressIndicatorTheme:
        ProgressIndicatorThemeData(color: colors.accent),
  );
}

/// 由配置值（Default/Light/Dark）解析 ThemeMode。
ThemeMode themeModeFromName(String name) => switch (name) {
      'Light' => ThemeMode.light,
      'Dark' => ThemeMode.dark,
      _ => ThemeMode.system,
    };
