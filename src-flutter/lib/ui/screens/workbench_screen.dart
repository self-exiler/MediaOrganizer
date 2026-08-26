/// 整理工作台屏（原型屏幕 1）：步骤条 + 目录卡片 + 执行参数卡片 +
/// 空态/分析中/完成态三区 + 底部操作栏。
library;

import 'package:flutter/material.dart';

import '../theme.dart';
import '../../viewmodels/workbench_view_model.dart';
import '../widgets.dart';

class WorkbenchScreen extends StatelessWidget {
  final WorkbenchViewModel vm;

  const WorkbenchScreen({super.key, required this.vm});

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: vm,
      builder: (context, _) {
        final c = AppColors.of(context);
        return Column(
          children: [
            Expanded(
              child: SingleChildScrollView(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    _buildSteps(c),
                    const SizedBox(height: 12),
                    _buildDirCard(context, c),
                    _buildParamsCard(context),
                    if (vm.phase == '') ...[
                      if (!vm.hasResult)
                        const InfoBar(
                          '👆 目录就绪。点击底部「开始分析」扫描源目录并提取拍摄日期；'
                          '分析完成确认计划后再执行归档。',
                        )
                      else
                        _buildResultCard(context),
                    ],
                    if (vm.phase == 'analyze') _buildAnalyzingCard(context),
                    if (vm.phase == 'execute')
                      InfoBar('正在执行归档… ${(vm.progress).toStringAsFixed(0)}%',
                          kind: InfoKind.info),
                  ],
                ),
              ),
            ),
            _buildBottomBar(context),
          ],
        );
      },
    );
  }

  Widget _buildSteps(AppColors c) {
    // 步骤推导：选目录（源目录已设）/ 分析（有结果或分析中）/ 执行
    var current = 0;
    final done = <int>{};
    if (vm.sourceDir.isNotEmpty) done.add(0);
    if (vm.phase == 'analyze') {
      current = 1;
    } else if (vm.phase == 'execute') {
      done.addAll([0, 1]);
      current = 2;
    } else if (vm.hasResult) {
      done.addAll([0, 1]);
      current = 2;
    }
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 8),
      child: StepIndicator(
        labels: const ['选目录', '分析', '执行'],
        currentIndex: current,
        doneIndexes: done,
      ),
    );
  }

  Widget _buildDirCard(BuildContext context, AppColors c) {
    // 输出目标下拉：本地 + 网络位置 + “添加网络位置…”
    final options = <String, String>{
      for (var i = 0; i < vm.outputTargetOptions.length; i++)
        i.toString(): vm.outputTargetOptions[i],
      vm.outputTargetOptions.length.toString(): '+ 添加网络位置…',
    };
    final isNetwork = vm.outputTargetIndex > 0;
    return MoCard(
      title: const CardTitle('目录', icon: '📁'),
      children: [
        InputBoxField(
          label: '源目录',
          text: vm.sourceDir,
          mono: true,
          pickLabel: '选取',
          onPick: vm.browseSource,
          onChanged: vm.setSourceDir,
          hint: '选择要整理的照片/视频所在文件夹',
        ),
        SelectField<String>(
          label: '输出目标',
          items: options,
          value: vm.outputTargetIndex.clamp(0, options.length - 1).toString(),
          onChanged: (v) =>
              v == null ? null : vm.selectOutputTarget(int.parse(v)),
        ),
        if (isNetwork)
          const InfoBar(
            'ℹ️ 网络目标（SMB / WebDAV）仅支持「复制」，已自动切换并禁用移动。',
            kind: InfoKind.warn,
          ),
        InputBoxField(
          label: '待处理目录（失败文件）',
          text: vm.pendingDir,
          mono: true,
          pickLabel: '选取',
          onPick: vm.browsePending,
          onChanged: vm.setPendingDir,
          hint: '批量移动失败文件的目标位置',
        ),
      ],
    );
  }

  Widget _buildParamsCard(BuildContext context) {
    return MoCard(
      title: const CardTitle('执行参数', icon: '⚙️'),
      children: [
        SelectField<int>(
          label: '分级目录',
          items: {
            0: '按天（2024/01/15/）',
            1: '按月（2024/01/）',
            2: '按年（2024/）',
          },
          value: vm.levelIndex,
          onChanged: (v) => v == null ? null : vm.setLevelIndex(v),
        ),
        SelectField<int>(
          label: '操作类型',
          items: {0: '复制（copy）', 1: '移动（move）'},
          value: vm.operationIndex.clamp(0, 1),
          disabledItems: vm.isMoveAllowed ? const {} : const {1},
          onChanged: (v) => v == null ? null : vm.setOperationIndex(v),
        ),
        SelectField<int>(
          label: '同名处理',
          items: {0: '跳过（skip）', 1: '覆盖（overwrite）', 2: '重命名（rename）'},
          value: vm.existActionIndex.clamp(0, 2),
          onChanged: (v) => v == null ? null : vm.setExistActionIndex(v),
        ),
        ToggleRow(
          label: 'mtime 矫正',
          sub: '将目标文件修改时间设为拍摄日期',
          value: vm.fixMtime,
          onChanged: vm.setFixMtime,
        ),
      ],
    );
  }

  Widget _buildAnalyzingCard(BuildContext context) {
    final c = AppColors.of(context);
    return MoCard(
      title: const CardTitle('正在分析…', icon: '⏳'),
      children: [
        MoProgressBar(percent: vm.totalFiles > 0 ? vm.progress : null),
        Text(
          '已处理 ${_n0(vm.successFiles + vm.failedFiles)} / ${_n0(vm.totalFiles)} · 成功 ${_n0(vm.successFiles)} · 失败 ${_n0(vm.failedFiles)}',
          style: TextStyle(fontSize: 12, color: c.text3),
        ),
      ],
    );
  }

  Widget _buildResultCard(BuildContext context) {
    final c = AppColors.of(context);
    return MoCard(
      title: CardTitle('分析结果',
          icon: '📊',
          link: '查看 ${_n0(vm.failedFiles)} 个失败 →',
          onLink: () => vm.onNavigateToFailed?.call()),
      children: [
        StatGrid(tiles: [
          StatTile(value: _n0(vm.totalFiles), label: '总计'),
          StatTile(value: _n0(vm.successFiles), label: '成功', color: c.success),
          StatTile(value: _n0(vm.failedFiles), label: '失败', color: c.error),
          StatTile(value: vm.successRate, label: '成功率'),
        ]),
        MoProgressBar(
            percent:
                vm.totalFiles == 0 ? 0 : vm.successFiles / vm.totalFiles * 100),
        if (vm.sourceSummary.isNotEmpty)
          Text('来源：${vm.sourceSummary}',
              style: TextStyle(fontSize: 12, color: c.text3)),
        const SizedBox(height: 6),
        InfoBar('✓ $planDisplay', kind: InfoKind.ok),
      ],
    );
  }

  String get planDisplay => vm.planText;

  Widget _buildBottomBar(BuildContext context) {
    if (vm.phase == 'analyze') {
      return BottomBar(children: [
        MoButton.danger('✕ 取消分析', onPressed: vm.cancelAnalysis),
      ]);
    }
    if (vm.phase == 'execute') {
      return BottomBar(children: [
        MoButton.danger('✕ 取消执行', onPressed: vm.cancelExecute),
      ]);
    }
    if (!vm.hasResult) {
      return BottomBar(children: [
        MoButton('▶ 开始分析', onPressed: vm.sourceDir.isEmpty ? null : vm.analyze),
      ]);
    }
    return BottomBar(children: [
      MoButton.secondary('↻ 重新分析', onPressed: vm.analyze),
      MoButton('▶ 执行归档',
          onPressed: vm.canExecute && !vm.isBusy ? vm.executeFiles : null),
    ]);
  }
}

String _n0(num v) {
  final s = v.toInt().toString();
  final b = StringBuffer();
  for (var i = 0; i < s.length; i++) {
    b.write(s[i]);
    final rem = s.length - i - 1;
    if (rem > 0 && rem % 3 == 0) b.write(',');
  }
  return b.toString();
}
