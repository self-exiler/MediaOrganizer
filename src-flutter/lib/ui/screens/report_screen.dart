/// 分析报告屏（原型屏幕 3）：等宽字体报告文本 + 另存为 / 分享。
library;

import 'package:flutter/material.dart';

import '../theme.dart';
import '../../viewmodels/report_view_model.dart';
import '../widgets.dart';

class ReportScreen extends StatelessWidget {
  final ReportViewModel vm;

  const ReportScreen({super.key, required this.vm});

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return AnimatedBuilder(
      animation: vm,
      builder: (context, _) => Column(
        children: [
          Expanded(
            child: ListView(
              padding: const EdgeInsets.all(16),
              children: [
                MoCard(
                  title: CardTitle('分析报告', icon: '📄', tag: vm.reportMeta),
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 12),
                  children: [
                    Container(
                      width: double.infinity,
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: Theme.of(context).brightness == Brightness.light
                            ? const Color(0xFFFAFAFA)
                            : c.rail,
                        borderRadius: BorderRadius.circular(8),
                        border: Border.all(color: c.cardBorder),
                      ),
                      child: SelectableText(
                        vm.reportText,
                        style: TextStyle(
                          fontSize: 11,
                          height: 1.6,
                          fontFamily: 'monospace',
                          color: c.text,
                        ),
                      ),
                    ),
                  ],
                ),
                Text('报告自动保存于应用专有目录，可另存为或分享导出。',
                    style: TextStyle(fontSize: 12, color: c.text3)),
                const SizedBox(height: 8),
              ],
            ),
          ),
          BottomBar(children: [
            MoButton.secondary('📥 另存为',
                onPressed: vm.hasReport
                    ? () async {
                        final msg = await vm.saveReport();
                        if (context.mounted) {
                          ScaffoldMessenger.of(context).showSnackBar(
                              SnackBar(content: Text(msg), width: 320));
                        }
                      }
                    : null),
            MoButton(
              '📤 分享',
              onPressed: vm.hasReport ? () => vm.shareReport() : null,
            ),
          ]),
        ],
      ),
    );
  }
}
