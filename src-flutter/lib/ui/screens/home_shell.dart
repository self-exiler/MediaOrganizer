/// 应用外壳（FR-A7）：顶部 AppBar（汉堡 + 标题）+ 侧滑抽屉导航 +
/// 四屏 IndexedStack + 底部固定状态栏（忙碌圆点 + 状态文本 + 上次分析摘要）。
/// 返回键行为由 Scaffold/Drawer 默认处理：抽屉打开时返回键先关抽屉。
library;

import 'package:flutter/material.dart';

import '../theme.dart';
import '../../viewmodels/main_view_model.dart';
import 'failed_files_screen.dart';
import 'report_screen.dart';
import 'settings_screen.dart';
import 'workbench_screen.dart';

class HomeShell extends StatelessWidget {
  final MainViewModel vm;

  const HomeShell({super.key, required this.vm});

  @override
  Widget build(BuildContext context) {
    // 工作台 → 其他页面的导航接线
    vm.workbench
      ..onNavigateToSettings = () => vm.navigate(3)
      ..onNavigateToFailed = () => vm.navigate(1);
    return AnimatedBuilder(
      animation: vm,
      builder: (context, _) {
        final c = AppColors.of(context);
        return Scaffold(
          backgroundColor: c.bg,
          appBar: AppBar(
            leading: Builder(
              builder: (drawerContext) => IconButton(
                icon: const Icon(Icons.menu),
                onPressed: () => Scaffold.of(drawerContext).openDrawer(),
              ),
            ),
            title: Text(vm.appTitle),
          ),
          drawer: _Drawer(vm: vm),
          body: Column(
            children: [
              Expanded(
                child: IndexedStack(
                  index: vm.selectedPage,
                  children: [
                    WorkbenchScreen(vm: vm.workbench),
                    FailedFilesScreen(vm: vm.failedFiles),
                    ReportScreen(vm: vm.report),
                    SettingsScreen(vm: vm.settings),
                  ],
                ),
              ),
              _StatusBar(vm: vm),
            ],
          ),
        );
      },
    );
  }
}

/// 抽屉（.drawer）：头部 logo、导航项（选中态左侧高亮条 + 淡色底 + 角标）、页脚版本。
class _Drawer extends StatelessWidget {
  final MainViewModel vm;

  const _Drawer({required this.vm});

  static const _icons = [Icons.home_outlined, Icons.warning_amber_rounded,
      Icons.description_outlined, Icons.settings_outlined];

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Drawer(
      backgroundColor: c.rail,
      width: 280,
      child: SafeArea(
        bottom: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // 头部
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 18, 20, 10),
              child: Row(
                children: [
                  Container(
                    width: 36,
                    height: 36,
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(8),
                      gradient: const LinearGradient(
                        begin: Alignment.topLeft,
                        end: Alignment.bottomRight,
                        colors: [Color(0xFF0067C0), Color(0xFF4FA3E3)],
                      ),
                    ),
                    alignment: Alignment.center,
                    child: const Text('MO',
                        style: TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.bold,
                            color: Colors.white)),
                  ),
                  const SizedBox(width: 12),
                  Text('MediaOrganizer',
                      style: TextStyle(
                          fontSize: 15,
                          fontWeight: FontWeight.w600,
                          color: c.text)),
                ],
              ),
            ),
            const SizedBox(height: 8),
            // 导航项
            for (final item in vm.navItems)
              _NavItemTile(
                item: item,
                selected: item.page == vm.selectedPage,
                icon: _icons[item.page],
                onTap: () {
                  Navigator.of(context).pop(); // 关抽屉
                  vm.navigate(item.page);
                },
              ),
            const Spacer(),
            // 页脚
            Container(
              decoration:
                  Border(top: BorderSide(color: c.cardBorder)).asBoxDecoration(),
              padding: const EdgeInsets.fromLTRB(20, 14, 20, 16),
              child: Text('v1.0-flutter · 开源 MIT\nAPK 侧载分发（GitHub Release）',
                  style: TextStyle(fontSize: 11, height: 1.5, color: c.text3)),
            ),
          ],
        ),
      ),
    );
  }
}

extension on Border {
  BoxDecoration asBoxDecoration() => BoxDecoration(border: this);
}

class _NavItemTile extends StatelessWidget {
  final NavItem item;
  final bool selected;
  final IconData icon;
  final VoidCallback onTap;

  const _NavItemTile({
    required this.item,
    required this.selected,
    required this.icon,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        child: Stack(
          children: [
            Container(
              constraints: const BoxConstraints(minHeight: 48),
              margin: const EdgeInsets.symmetric(vertical: 1),
              padding: const EdgeInsets.symmetric(horizontal: 20),
              alignment: Alignment.centerLeft,
              decoration: selected
                  ? BoxDecoration(
                      color: c.accent.withOpacity(0.08),
                      borderRadius: const BorderRadius.horizontal(
                          right: Radius.circular(24)))
                  : null,
              child: Row(
                children: [
                  Icon(icon, size: 20, color: selected ? c.accent : c.text2),
                  const SizedBox(width: 16),
                  Expanded(
                    child: Text(item.label,
                        style: TextStyle(
                            fontSize: 14,
                            fontWeight: selected ? FontWeight.w500 : FontWeight.w400,
                            color: selected ? c.accent : c.text2)),
                  ),
                  if (item.hasBadge)
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                      decoration: BoxDecoration(
                        color: c.error,
                        borderRadius: BorderRadius.circular(10),
                      ),
                      child: Text('${item.badge}',
                          style: const TextStyle(
                              fontSize: 11, color: Colors.white)),
                    ),
                ],
              ),
            ),
            if (selected)
              Positioned(
                left: 0,
                top: 10,
                bottom: 10,
                child: Container(width: 3, decoration: BoxDecoration(color: c.accent, borderRadius: BorderRadius.circular(2))),
              ),
          ],
        ),
      ),
    );
  }
}

/// 底部状态栏（FR-A7.2）：忙碌圆点（busy 时呼吸动画）+ 状态文本 + 右侧摘要。
class _StatusBar extends StatelessWidget {
  final MainViewModel vm;

  const _StatusBar({required this.vm});

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Container(
      height: 32,
      decoration: BoxDecoration(
        color: c.card,
        border: Border(top: BorderSide(color: c.cardBorder)),
      ),
      padding: const EdgeInsets.symmetric(horizontal: 16),
      child: Row(
        children: [
          if (vm.isBusy)
            _PulsingDot(color: c.accent)
          else
            Container(
              width: 8,
              height: 8,
              decoration: BoxDecoration(color: c.success, shape: BoxShape.circle),
            ),
          const SizedBox(width: 6),
          Expanded(
            child: Text(vm.statusText,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 11, color: c.text2)),
          ),
          const SizedBox(width: 12),
          Flexible(
            child: Text(vm.statusDetail,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 11, color: c.text3)),
          ),
        ],
      ),
    );
  }
}

class _PulsingDot extends StatefulWidget {
  final Color color;

  const _PulsingDot({required this.color});

  @override
  State<_PulsingDot> createState() => _PulsingDotState();
}

class _PulsingDotState extends State<_PulsingDot>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
      vsync: this, duration: const Duration(milliseconds: 900))
    ..repeat(reverse: true);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) =>
      FadeTransition(
        opacity: Tween(begin: 0.35, end: 1.0).animate(_controller),
        child: Container(
          width: 8,
          height: 8,
          decoration:
              BoxDecoration(color: widget.color, shape: BoxShape.circle),
        ),
      );
}
