/// 失败文件屏（原型屏幕 2）：按结构指纹聚类分组列表 + 点击展开详情 +
/// 勾选批量移动到待处理目录（内联确认条）。
library;

import 'package:flutter/material.dart';

import '../theme.dart';
import '../../viewmodels/failed_files_view_model.dart';
import '../widgets.dart';

class FailedFilesScreen extends StatefulWidget {
  final FailedFilesViewModel vm;

  const FailedFilesScreen({super.key, required this.vm});

  @override
  State<FailedFilesScreen> createState() => _FailedFilesScreenState();
}

class _FailedFilesScreenState extends State<FailedFilesScreen> {
  List<FailedItem>? _pendingConfirm; // 待确认的批量移动集合（非 null 时显示确认条）

  @override
  Widget build(BuildContext context) {
    final vm = widget.vm;
    return AnimatedBuilder(
      animation: vm,
      builder: (context, _) {
        final c = AppColors.of(context);
        return Column(
          children: [
            Expanded(
              child: vm.items.isEmpty
                  ? ListView(
                      padding: const EdgeInsets.all(16),
                      children: [
                        InfoBar(
                          vm.moveHint.isEmpty
                              ? '暂无失败文件。完成一次分析后，未能提取日期的文件会集中在这里。'
                              : vm.moveHint,
                          kind:
                              vm.moveHint.isEmpty ? InfoKind.info : InfoKind.warn,
                        ),
                      ],
                    )
                  : ListView(
                      padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
                      children: [
                        InfoBar(
                          '${vm.items.length} 个文件未能提取日期，已按文件名结构指纹聚类。'
                          '可批量移动到待处理目录。',
                          kind: InfoKind.warn,
                        ),
                        if (vm.isMoving) ...[
                          MoProgressBar(percent: vm.moveProgress),
                          Text('正在移动… ${(vm.moveProgress).toStringAsFixed(0)}%',
                              style:
                                  TextStyle(fontSize: 12, color: c.text3)),
                          const SizedBox(height: 8),
                        ],
                        _buildGroupedList(context),
                      ],
                    ),
            ),
            // 内联确认条 / 结果提示
            if (_pendingConfirm != null)
              Container(
                margin: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                padding:
                    const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                decoration: BoxDecoration(
                  color: AppColors.of(context)
                      .warning
                      .withOpacity(0.08),
                  border:
                      Border.all(color: AppColors.of(context).warning.withOpacity(0.45)),
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(
                        '确认将选中的 ${_pendingConfirm!.length} 个文件移动到待处理目录？（移动 = 复制 + 删除源）',
                        style: TextStyle(
                            fontSize: 13,
                            height: 1.4,
                            color: AppColors.of(context).warning),
                      ),
                    ),
                    MoSmallButton('取消',
                        onPressed: () => setState(() => _pendingConfirm = null)),
                    const SizedBox(width: 8),
                    MoSmallButton('确认', onPressed: () => _confirmMove()),
                  ],
                ),
              ),
            if (!_isHintEmptyOrStale(vm))
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                child: Align(
                  alignment: Alignment.centerLeft,
                  child: Text(vm.moveHint,
                      style: TextStyle(fontSize: 12, color: c.text2)),
                ),
              ),
            BottomBar(children: [
              MoButton.secondary(
                vm.shouldSelectAll ? '全选' : '取消全选',
                onPressed: vm.isMoving ? null : vm.toggleSelectAll,
              ),
              MoButton(
                '📁 批量移动到待处理${vm.checkedCount > 0 ? '（已选 ${vm.checkedCount}）' : ''}',
                onPressed: vm.isMoving || vm.items.isEmpty ? null : _requestMove,
              ),
            ]),
          ],
        );
      },
    );
  }

  bool _isHintEmptyOrStale(FailedFilesViewModel vm) => vm.moveHint.isEmpty;

  /// 指纹相邻排序后按变化点插入组标题行。
  Widget _buildGroupedList(BuildContext context) {
    final c = AppColors.of(context);
    final children = <Widget>[];
    String? lastFp;
    var countInGroup = 0;
    void closeGroup() {
      children.add(Padding(
        padding: const EdgeInsets.only(bottom: 4),
        child: Align(
          alignment: Alignment.centerLeft,
          child: Text('指纹 $lastFp（$countInGroup）',
              style: TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.w600,
                  color: c.text3)),
        ),
      ));
    }

    for (final item in vm.items) {
      if (item.fingerprint != lastFp) {
        if (lastFp != null) closeGroup();
        lastFp = item.fingerprint;
        countInGroup = 0;
      }
      countInGroup++;
      children.add(_FailedItemTile(
        item: item,
        expanded: vm.expandedPaths.contains(item.path),
        onToggleExpand: () => vm.toggleExpanded(item.path),
        onToggleCheck: () => vm.toggleItem(item),
        onOpen: () => vm.openWithSystem(item),
      ));
    }
    if (lastFp != null) closeGroup();
    return Card(
      color: c.card,
      elevation: 0,
      margin: const EdgeInsets.only(bottom: 12),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(c.radius),
        side: BorderSide(color: c.cardBorder),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 8),
        child: Column(children: children),
      ),
    );
  }

  void _requestMove() {
    final selected = widget.vm.itemsToMove();
    if (selected.isEmpty) return;
    if (widget.vm.pendingDir.trim().isEmpty) {
      widget.vm.moveConfirmed(selected); // VM 会提示“请先指定待处理目录”
      return;
    }
    setState(() => _pendingConfirm = selected);
  }

  Future<void> _confirmMove() async {
    final selected = _pendingConfirm;
    if (selected == null) return;
    setState(() => _pendingConfirm = null);
    await widget.vm.moveConfirmed(selected);
  }
}

class _FailedItemTile extends StatelessWidget {
  final FailedItem item;
  final bool expanded;
  final VoidCallback onToggleExpand;
  final VoidCallback onToggleCheck;
  final VoidCallback onOpen;

  const _FailedItemTile({
    required this.item,
    required this.expanded,
    required this.onToggleExpand,
    required this.onToggleCheck,
    required this.onOpen,
  });

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    final parent = item.path.replaceAll('\\', '/').split('/')..removeLast();
    final shortParent =
        parent.isEmpty ? item.path : parent.last;
    return AnimatedBuilder(
      animation: item,
      builder: (context, _) {
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            InkWell(
              onTap: onToggleExpand,
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 12),
                child: Row(
                  children: [
                    // 圆形勾选框（.check）
                    GestureDetector(
                      onTap: onToggleCheck,
                      child: Container(
                        width: 24,
                        height: 24,
                        decoration: BoxDecoration(
                          shape: BoxShape.circle,
                          border: Border.all(
                            color: item.isChecked ? c.accent : Colors.grey.shade400,
                            width: 2,
                          ),
                          color: item.isChecked ? c.accent : Colors.transparent,
                        ),
                        child: item.isChecked
                            ? const Icon(Icons.check,
                                size: 14, color: Colors.white)
                            : null,
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(item.name,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(
                                  fontSize: 13,
                                  fontWeight: FontWeight.w500,
                                  color: c.text)),
                          const SizedBox(height: 2),
                          Text('.../$shortParent · ${item.sizeLabel}',
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style:
                                  TextStyle(fontSize: 11, color: c.text3)),
                          const SizedBox(height: 2),
                          ChipLabel(item.reason, kind: ChipKind.err),
                        ],
                      ),
                    ),
                    Icon(
                      expanded
                          ? Icons.keyboard_arrow_down
                          : Icons.keyboard_arrow_right,
                      size: 18,
                      color: c.text3,
                    ),
                  ],
                ),
              ),
            ),
            if (expanded)
              Padding(
                padding: const EdgeInsets.fromLTRB(36, 0, 0, 10),
                child: DefaultTextStyle(
                  style: TextStyle(fontSize: 12, height: 1.7, color: c.text2),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('完整路径：${item.path}'),
                      Text('大小：${item.sizeLabel} · 失败原因：${item.reason}'),
                      GestureDetector(
                        onTap: onOpen,
                        child: Text('↗ 用系统应用打开',
                            style: TextStyle(
                                fontSize: 12,
                                fontWeight: FontWeight.w500,
                                color: c.accent)),
                      ),
                    ],
                  ),
                ),
              ),
            Divider(height: 1, color: c.cardBorder.withOpacity(0.6)),
          ],
        );
      },
    );
  }
}
