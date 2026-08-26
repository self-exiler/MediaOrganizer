/// 设置屏（原型屏幕 4）：分组卡片——提取器 / 文件名模式 / 网络位置 / 扫描参数 /
/// 通用（主题、恢复默认）。所有改动即时落盘并通知工作台重建提取链。
library;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/app_config.dart' show NetworkProfile;
import '../../core/enums.dart' show NetworkType;
import '../../services/credential_crypto.dart';
import '../theme.dart';
import '../../viewmodels/settings_view_model.dart';
import '../widgets.dart';

class SettingsScreen extends StatefulWidget {
  final SettingsViewModel vm;

  const SettingsScreen({super.key, required this.vm});

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  late List<ExtractorSettingVM> _extractors;

  @override
  void initState() {
    super.initState();
    // 缓存提取器行状态，避免滑块拖动中因重建丢进度
    _extractors = widget.vm.buildExtractors();
  }

  SettingsViewModel get vm => widget.vm;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: vm,
      builder: (context, _) => ListView(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
        children: [
          _buildExtractorsSection(context),
          _buildPatternsSection(context),
          _buildNetworkSection(context),
          _buildScanSection(context),
          _buildGeneralSection(context),
        ],
      ),
    );
  }

  // ==================== 提取器 ====================

  Widget _buildExtractorsSection(BuildContext context) {
    final c = AppColors.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionTitle('提取器'),
        MoCard(
          children: [
            for (final e in _extractors) ...[
              ToggleRow(
                label: e.displayLabel,
                sub: switch (e.name) {
                  'Exif' => '照片 EXIF / 视频容器元数据',
                  'FileName' => '用正则模式从文件名解析日期',
                  'FileSystem' => '用文件修改时间兜底（默认禁用）',
                  _ => null,
                },
                value: e.enabled,
                onChanged: (v) {
                  e.setEnabled(v);
                  setState(() {});
                },
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(0, 0, 8, 8),
                child: Row(
                  children: [
                    Text('权重', style: TextStyle(fontSize: 12, color: c.text2)),
                    Expanded(
                      child: Slider(
                        value: e.weight.clamp(0, 2),
                        min: 0,
                        max: 2,
                        divisions: 20,
                        onChanged: (v) => setState(() => e.setWeight(v)),
                        onChangeEnd: (_) => e.commitWeight(),
                      ),
                    ),
                    SizedBox(
                      width: 32,
                      child: Text(e.weight.toStringAsFixed(1),
                          textAlign: TextAlign.right,
                          style: TextStyle(
                              fontSize: 12,
                              fontWeight: FontWeight.w600,
                              color: c.accent)),
                    ),
                  ],
                ),
              ),
              Divider(height: 1, color: c.cardBorder),
              const SizedBox(height: 6),
            ],
            SelectField<int>(
              label: '最大年份回溯（max_years_past）',
              items: {
                for (final y in SettingsViewModel.maxYearsPastOptions)
                  y: '$y 年',
              },
              value: vm.maxYearsPast,
              onChanged: (v) => v == null ? null : vm.setMaxYearsPast(v),
            ),
            SelectField<int>(
              label: '未来日期缓冲（future_date_buffer_days）',
              items: {
                0: '0 天（未来日期归入 FutureDate/）',
                1: '1 天',
                7: '7 天',
              },
              value: vm.futureDateBufferDays,
              onChanged: (v) => v == null ? null : vm.setFutureBufferDays(v),
            ),
          ],
        ),
      ],
    );
  }

  // ==================== 文件名模式 ====================

  Widget _buildPatternsSection(BuildContext context) {
    final c = AppColors.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionTitle('文件名模式'),
        MoCard(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
          title: Row(
            children: [
              Text('模式清单 · ${vm.patternSummary}',
                  style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w600,
                      color: c.text)),
              const Spacer(),
              GestureDetector(
                onTap: () {
                  vm.restoreDefaultPatterns();
                  ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
                      content: Text('已恢复全部内置模式并保存'), width: 300));
                },
                child: Text('恢复内置',
                    style: TextStyle(fontSize: 12, color: c.accent)),
              ),
            ],
          ),
          children: [
            for (final p in vm.patterns) ...[
              Row(
                children: [
                  Container(
                    width: 36,
                    height: 36,
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: p.enabled
                          ? (Theme.of(context).brightness == Brightness.light
                              ? const Color(0xFFE9F7E9)
                              : const Color(0xFF123B12))
                          : (Theme.of(context).brightness == Brightness.light
                              ? const Color(0xFFF0F0F0)
                              : c.rail),
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Icon(Icons.check,
                        size: 16,
                        color: p.enabled
                            ? AppColors.of(context).success
                            : c.text3),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(p.name,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.w500,
                                color: c.text)),
                        const SizedBox(height: 2),
                        Text('${p.regex} · ${p.sourceLabel}',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                                fontSize: 11,
                                fontFamily: 'monospace',
                                color: c.text3)),
                      ],
                    ),
                  ),
                  Switch(
                    value: p.enabled,
                    onChanged: (v) => vm.setPatternEnabled(p, v),
                  ),
                  IconButton(
                    tooltip: '删除模式',
                    icon: Icon(Icons.delete_outline, size: 18, color: c.text3),
                    onPressed: () {
                      vm.deletePattern(p);
                      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
                          content: Text('已删除「${p.name}」并保存'), width: 280));
                    },
                  ),
                ],
              ),
              Divider(height: 1, color: c.cardBorder.withOpacity(0.6)),
            ],
            InfoBar(
              'ℹ️ 如需新增自定义模式，请在桌面版魔术工具中编辑后把 patterns.json 复制到手机。',
              kind: InfoKind.info,
            ),
          ],
        ),
      ],
    );
  }

  // ==================== 网络位置 ====================

  Widget _buildNetworkSection(BuildContext context) {
    final c = AppColors.of(context);
    final profiles = vm.networkProfiles;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionTitle('网络位置'),
        MoCard(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
          children: [
            if (profiles.isEmpty)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 10),
                child: Text('尚未添加网络位置。输出目标支持 WebDAV；SMB 暂不支持。',
                    style: TextStyle(fontSize: 12, color: c.text3)),
              ),
            for (final p in profiles)
              Row(
                children: [
                  Container(
                    width: 36,
                    height: 36,
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: Theme.of(context).brightness == Brightness.light
                          ? const Color(0xFFF0F0F0)
                          : c.rail,
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Text(p.typeLabel == 'SMB' ? '🖥' : '🌐',
                        style: const TextStyle(fontSize: 15)),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: GestureDetector(
                      onTap: () => _showNetworkEditor(context, p),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(p.name,
                              style: TextStyle(
                                  fontSize: 13,
                                  fontWeight: FontWeight.w500,
                                  color: c.text)),
                          const SizedBox(height: 2),
                          Row(children: [
                            ChipLabel(p.typeLabel, kind: ChipKind.smb),
                            const SizedBox(width: 6),
                            Flexible(
                              child: Text(p.address,
                                  overflow: TextOverflow.ellipsis,
                                  style: TextStyle(
                                      fontSize: 11,
                                      fontFamily: 'monospace',
                                      color: c.text3)),
                            ),
                          ]),
                          Text(p.verifiedLabel,
                              style:
                                  TextStyle(fontSize: 10, color: c.text3)),
                        ],
                      ),
                    ),
                  ),
                  MoSmallButton('测试', onPressed: () => _testNetwork(context, p)),
                  IconButton(
                    tooltip: '删除',
                    icon: Icon(Icons.delete_outline, size: 18, color: c.text3),
                    onPressed: () => _confirmDeleteNetwork(context, p),
                  ),
                ],
              ),
            Row(children: [
              MoButton.secondary('+ 添加网络位置…',
                  onPressed: () => _showNetworkEditor(context, null)),
            ]),
            const InfoBar(
              'ℹ️ 网络目标仅支持复制（copy）；WebDAV 走 HTTP Basic 认证。SMB 需在桌面版执行。',
              kind: InfoKind.info,
            ),
          ],
        ),
      ],
    );
  }

  Future<void> _testNetwork(BuildContext context, NetworkProfileVM p) async {
    ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('正在测试「${p.name}」…'), width: 260));
    final (ok, message) = await vm.testNetwork(p.source);
    if (!mounted) return;
    ScaffoldMessenger.of(context).hideCurrentSnackBar();
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text('${ok ? "✓" : "✗"} 「${p.name}」$message'),
        width: 320));
  }

  Future<void> _confirmDeleteNetwork(
      BuildContext context, NetworkProfileVM p) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('删除网络位置'),
        content: Text('确定删除「${p.name}」吗？工作台将回退到本地目标。'),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(ctx, false),
              child: const Text('取消')),
          TextButton(
              onPressed: () => Navigator.pop(ctx, true),
              child: const Text('删除')),
        ],
      ),
    );
    if (confirmed ?? false) {
      vm.deleteNetwork(p.name);
    }
  }

  /// 新建/编辑网络位置（底部弹层表单）。
  Future<void> _showNetworkEditor(
      BuildContext context, NetworkProfileVM? existing) async {
    final nameCtrl =
        TextEditingController(text: existing?.name ?? '');
    final addrCtrl = TextEditingController(text: existing?.address ?? '');
    final userCtrl =
        TextEditingController(text: existing?.source.username ?? '');
    final passCtrl = TextEditingController();
    // 分段按钮：0 = WebDAV，1 = SMB
    var typeIndex = existing?.typeLabel == 'WebDAV' ? 0 : 1;
    String hint = '';

    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (sheetContext) => StatefulBuilder(
        builder: (sheetContext, setSheetState) => Padding(
          padding: EdgeInsets.only(
            left: 20,
            right: 20,
            top: 20,
            bottom: MediaQuery.of(sheetContext).viewInsets.bottom + 20,
          ),
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(existing == null ? '添加网络位置' : '编辑「${existing.name}」',
                    style: TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w600,
                        color: AppColors.of(sheetContext).text)),
                const SizedBox(height: 16),
                TextField(
                  controller: nameCtrl,
                  decoration: const InputDecoration(
                      labelText: '名称', border: OutlineInputBorder()),
                ),
                const SizedBox(height: 12),
                SegmentedButton<int>(
                  segments: const [
                    ButtonSegment(value: 0, label: Text('WebDAV')),
                    ButtonSegment(value: 1, label: Text('SMB（暂不支持）')),
                  ],
                  selected: {typeIndex},
                  onSelectionChanged: (s) =>
                      setSheetState(() => typeIndex = s.first),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: addrCtrl,
                  decoration: const InputDecoration(
                      labelText: '地址（https://dav.example.com/photos）',
                      border: OutlineInputBorder()),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: userCtrl,
                  decoration: const InputDecoration(
                      labelText: '用户名', border: OutlineInputBorder()),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: passCtrl,
                  obscureText: true,
                  decoration: InputDecoration(
                    labelText: existing == null
                        ? '密码'
                        : '密码（留空则不修改）',
                    border: const OutlineInputBorder(),
                  ),
                ),
                const SizedBox(height: 8),
                Align(
                  alignment: Alignment.centerLeft,
                  child: Text(hint,
                      style: TextStyle(fontSize: 12, color: AppColors.of(sheetContext).text2)),
                ),
                const SizedBox(height: 8),
                Row(children: [
                  OutlinedButton.icon(
                    icon: const Icon(Icons.wifi_tethering, size: 16),
                    label: const Text('测试连接'),
                    onPressed: () async {
                      final profile = existing?.source.copy() ?? NetworkProfile();
                      profile
                        ..name = nameCtrl.text.trim()
                        ..type =
                            typeIndex == 0 ? NetworkType.webDav : NetworkType.smb
                        ..address = addrCtrl.text.trim()
                        ..username = userCtrl.text.trim()
                        ..password = passCtxForTest(existing, passCtrl);
                      setSheetState(() => hint = '正在测试连接…');
                      final (ok, message) = await vm.testNetwork(profile);
                      setSheetState(() => hint = '${ok ? "✓" : "✗"} $message');
                    },
                  ),
                ]),
                const SizedBox(height: 16),
                Row(children: [
                  Expanded(
                    child: OutlinedButton(
                      onPressed: () => Navigator.pop(sheetContext),
                      child: const Text('取消'),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: FilledButton(
                      onPressed: () {
                        final error = vm.saveNetwork(
                          originalName: existing?.name ?? '',
                          name: nameCtrl.text,
                          type: typeIndex == 0
                              ? NetworkType.webDav
                              : NetworkType.smb,
                          address: addrCtrl.text,
                          username: userCtrl.text,
                          password: passCtrl.text,
                        );
                        if (error != null) {
                          setSheetState(() => hint = error);
                          return;
                        }
                        Navigator.pop(sheetContext);
                        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
                            content:
                                Text('已保存「${nameCtrl.text.trim()}」；可在工作台选择该网络目标'),
                            width: 340));
                      },
                      child: const Text('保存'),
                    ),
                  ),
                ]),
              ],
            ),
          ),
        ),
      ),
    );
  }

  /// 测试用密码：新输入优先；编辑时留空则沿用已存密文。
  String passCtxForTest(
      NetworkProfileVM? existing, TextEditingController passCtrl) {
    if (passCtrl.text.isNotEmpty) {
      return CredentialCrypto.encrypt(passCtrl.text);
    }
    return existing?.source.password ?? '';
  }

  // ==================== 扫描参数 ====================

  Widget _buildScanSection(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionTitle('扫描参数'),
        MoCard(
          children: [
            ToggleRow(
              label: '扫描所有文件',
              sub: '忽略扩展名白名单',
              value: vm.scanAllFiles,
              onChanged: vm.setScanAllFiles,
            ),
            CommitTextField(
              label: '扩展名白名单（逗号分隔）',
              initialText: vm.supportedFormatsText,
              mono: true,
              enabled: !vm.scanAllFiles,
              onCommit: vm.setSupportedFormatsText,
            ),
            SelectField<int>(
              label: '执行并行度',
              items: {0: '自动（本地 2 / 网络 4）', 1: '1', 2: '2', 4: '4'},
              value: vm.executionParallelism,
              onChanged: (v) => v == null ? null : vm.setExecutionParallelism(v),
            ),
          ],
        ),
      ],
    );
  }

  // ==================== 通用 ====================

  Widget _buildGeneralSection(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionTitle('通用'),
        MoCard(
          children: [
            SelectField<int>(
              label: '主题',
              items: {0: '跟随系统', 1: '浅色', 2: '深色'},
              value: vm.themeIndex,
              onChanged: (v) => v == null ? null : vm.setThemeIndex(v),
            ),
            Row(children: [
              MoButton.secondary('↺ 恢复默认配置与内置模式', onPressed: () {
                final msg = vm.initialize();
                setState(() => _extractors = vm.buildExtractors());
                ScaffoldMessenger.of(context)
                    .showSnackBar(SnackBar(content: Text(msg), width: 320));
              }),
            ]),
          ],
        ),
      ],
    );
  }
}

/// 失焦/提交时才写回的文本框（避免每个按键都触发落盘与全局刷新）。
class CommitTextField extends StatefulWidget {
  final String label;
  final String initialText;
  final bool mono;
  final bool enabled;
  final ValueChanged<String> onCommit;

  const CommitTextField({
    super.key,
    required this.label,
    required this.initialText,
    required this.onCommit,
    this.mono = false,
    this.enabled = true,
  });

  @override
  State<CommitTextField> createState() => _CommitTextFieldState();
}

class _CommitTextFieldState extends State<CommitTextField> {
  late final TextEditingController _controller =
      TextEditingController(text: widget.initialText);
  late final FocusNode _focus = FocusNode()
    ..addListener(() {
      if (!_focus.hasFocus && _controller.text != widget.initialText) {
        widget.onCommit(_controller.text);
      }
    });

  @override
  void dispose() {
    _controller.dispose();
    _focus.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 7),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          FieldLabel(widget.label),
          const SizedBox(height: 6),
          Container(
            decoration: BoxDecoration(
              color: Theme.of(context).brightness == Brightness.light
                  ? const Color(0xFFF8F8F8)
                  : c.rail,
              border: Border.all(color: c.cardBorder),
              borderRadius: BorderRadius.circular(8),
            ),
            padding: const EdgeInsets.symmetric(horizontal: 14),
            constraints: const BoxConstraints(minHeight: 48),
            child: Center(
              child: TextField(
                controller: _controller,
                focusNode: _focus,
                enabled: widget.enabled,
                inputFormatters: [
                  FilteringTextInputFormatter.allow(RegExp(r'[a-zA-Z0-9,，;；\s.]')),
                ],
                onSubmitted: (v) => widget.onCommit(v),
                style: widget.mono
                    ? TextStyle(fontSize: 11, color: c.text, fontFamily: 'monospace')
                    : TextStyle(fontSize: 13, color: c.text),
                decoration: const InputDecoration(
                  isDense: true,
                  border: InputBorder.none,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
