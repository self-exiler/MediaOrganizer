/// 原型控件库：卡片 / InfoBar / 步骤条 / 统计块 / 开关行 / 下拉框 / 输入框 /
/// 徽章 / 底部操作栏 / 按钮（对应原型 index.html 的 .card/.infobar/.steps 等样式）。
library;

import 'package:flutter/material.dart';

import 'theme.dart';

/// 卡片容器（.card）。
class MoCard extends StatelessWidget {
  final Widget? title; // 标题行（h2）
  final List<Widget> children;
  final EdgeInsetsGeometry padding;

  const MoCard({
    super.key,
    this.title,
    this.children = const [],
    this.padding = const EdgeInsets.all(16),
  });

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: padding,
      decoration: BoxDecoration(
        color: c.card,
        borderRadius: BorderRadius.circular(c.radius),
        border: Border.all(color: c.cardBorder),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withOpacity(0.06),
            blurRadius: 3,
            offset: const Offset(0, 1),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          if (title != null) ...[title!, const SizedBox(height: 12)],
          ...children,
        ],
      ),
    );
  }
}

/// 卡片标题行（h2）：图标+标题 + 可选 tag 与右侧 link。
class CardTitle extends StatelessWidget {
  final String icon;
  final String text;
  final String? tag;
  final String? link;
  final VoidCallback? onLink;

  const CardTitle(this.text, {super.key, this.icon = '', this.tag, this.link, this.onLink});

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        if (icon.isNotEmpty) ...[
          Text(icon, style: const TextStyle(fontSize: 14)),
          const SizedBox(width: 8),
        ],
        Expanded(
          child: Text(text,
              style: TextStyle(
                  fontSize: 14, fontWeight: FontWeight.w600, color: c.text)),
        ),
        if (tag != null)
          Text(tag!,
              style: TextStyle(fontSize: 11, color: c.text3)),
        if (link != null)
          GestureDetector(
            onTap: onLink,
            child: Padding(
              padding: const EdgeInsets.only(left: 8),
              child: Text(link!,
                  style: TextStyle(fontSize: 12, color: c.accent)),
            ),
          ),
      ],
    );
  }
}

enum InfoKind { info, warn, ok }

/// 提示条（.infobar）。
class InfoBar extends StatelessWidget {
  final InfoKind kind;
  final String text;
  final List<Widget> actions;
  final IconData? leadingIcon;

  const InfoBar(this.text, {super.key, this.kind = InfoKind.info, this.actions = const [], this.leadingIcon});

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    final (bg, border, fg, icon) = switch (kind) {
      (InfoKind.warn) => (
          _blend(c.warning, context),
          c.warning.withOpacity(0.45),
          c.warning,
          Icons.warning_amber_rounded
        ),
      (InfoKind.ok) => (
          _blend(c.success, context),
          c.success.withOpacity(0.45),
          c.success,
          Icons.check_circle_outline
        ),
      (InfoKind.info) => (
          _blend(c.accent, context),
          c.accent.withOpacity(0.35),
          c.accent,
          Icons.info_outline
        ),
    };
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: border),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(leadingIcon ?? icon, size: 16, color: fg),
          const SizedBox(width: 10),
          Expanded(
            child: Text(text,
                style: TextStyle(fontSize: 13, height: 1.4, color: fg)),
          ),
          ...actions,
        ],
      ),
    );
  }

  static Color _blend(Color base, BuildContext context) =>
      Theme.of(context).brightness == Brightness.light
          ? Color.lerp(base, Colors.white, 0.92)!
          : Color.lerp(base, Colors.black, 0.75)!;
}

/// 统计块（.stat）与两列网格（.stat-grid）。
class StatGrid extends StatelessWidget {
  final List<StatTile> tiles;

  const StatGrid({super.key, required this.tiles});

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        for (var i = 0; i < tiles.length; i += 2)
          Padding(
            padding: EdgeInsets.only(bottom: i + 2 < tiles.length ? 10 : 0),
            child: Row(
              children: [
                Expanded(child: tiles[i]),
                const SizedBox(width: 10),
                if (i + 1 < tiles.length) Expanded(child: tiles[i + 1]),
              ],
            ),
          ),
      ],
    );
  }
}

class StatTile extends StatelessWidget {
  final String value;
  final String label;
  final Color? color;

  const StatTile({super.key, required this.value, required this.label, this.color});

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: Theme.of(context).brightness == Brightness.light
            ? const Color(0xFFFAFAFA)
            : c.rail,
        border: Border.all(color: c.cardBorder),
        borderRadius: BorderRadius.circular(10),
      ),
      child: Column(
        children: [
          Text(value,
              style: TextStyle(
                fontSize: 26,
                fontWeight: FontWeight.w700,
                color: color ?? c.accent,
                height: 1.1,
              )),
          const SizedBox(height: 2),
          Text(label, style: TextStyle(fontSize: 11, color: c.text3)),
        ],
      ),
    );
  }
}

/// 三步骤指示条（.steps）：选目录 → 分析 → 执行。
class StepIndicator extends StatelessWidget {
  final List<String> labels;
  final int currentIndex;
  final Set<int> doneIndexes;

  const StepIndicator({
    super.key,
    required this.labels,
    required this.currentIndex,
    this.doneIndexes = const {},
  });

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Row(
      children: [
        for (var i = 0; i < labels.length; i++) ...[
          if (i > 0) Expanded(child: Container(height: 2, color: c.cardBorder)),
          Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 22,
                height: 22,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: doneIndexes.contains(i)
                      ? c.success
                      : (i == currentIndex ? c.accent : c.cardBorder),
                ),
                child: doneIndexes.contains(i)
                    ? const Icon(Icons.check, size: 13, color: Colors.white)
                    : Text('${i + 1}',
                        style: const TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                            color: Colors.white)),
              ),
              const SizedBox(height: 4),
              Text(labels[i],
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight:
                        i == currentIndex ? FontWeight.w600 : FontWeight.w400,
                    color: doneIndexes.contains(i)
                        ? c.success
                        : (i == currentIndex ? c.accent : c.text3),
                  )),
            ],
          ),
        ],
      ],
    );
  }
}

/// 开关行（.toggle-row）。
class ToggleRow extends StatelessWidget {
  final String label;
  final String? sub;
  final bool value;
  final ValueChanged<bool> onChanged;

  const ToggleRow({
    super.key,
    required this.label,
    this.sub,
    required this.value,
    required this.onChanged,
  });

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label,
                    style: TextStyle(fontSize: 14, color: c.text)),
                if (sub != null) ...[
                  const SizedBox(height: 2),
                  Text(sub!,
                      style: TextStyle(fontSize: 12, color: c.text3)),
                ],
              ],
            ),
          ),
          Switch(value: value, onChanged: onChanged),
        ],
      ),
    );
  }
}

/// 字段标签（.field label）。
class FieldLabel extends StatelessWidget {
  final String text;

  const FieldLabel(this.text, {super.key});

  @override
  Widget build(BuildContext context) => Text(text,
      style: TextStyle(
          fontSize: 12,
          color: AppColors.of(context).text2,
          fontWeight: FontWeight.w500));
}

/// 下拉选择字段（select）。
class SelectField<T> extends StatelessWidget {
  final String? label;
  final Map<T, String> items; // 值 → 展示文本；disabled 条目文本前缀 '!' 
  final T value;
  final ValueChanged<T?> onChanged;
  final Set<T>? disabledItems;

  const SelectField({
    super.key,
    this.label,
    required this.items,
    required this.value,
    required this.onChanged,
    this.disabledItems,
  });

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (label != null) ...[FieldLabel(label!), const SizedBox(height: 6)],
        Container(
          decoration: BoxDecoration(
            color: Theme.of(context).brightness == Brightness.light
                ? const Color(0xFFF8F8F8)
                : c.rail,
            border: Border.all(color: c.cardBorder),
            borderRadius: BorderRadius.circular(8),
          ),
          padding: const EdgeInsets.symmetric(horizontal: 12),
          constraints: const BoxConstraints(minHeight: 48),
          child: DropdownButtonHideUnderline(
            child: DropdownButton<T>(
              value: value,
              isExpanded: true,
              icon: Icon(Icons.expand_more, size: 18, color: c.text2),
              dropdownColor: c.card,
              items: [
                for (final e in items.entries)
                  DropdownMenuItem<T>(
                    value: e.key,
                    enabled: !(disabledItems?.contains(e.key) ?? false),
                    child: Text(
                      e.value,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 13,
                        color: disabledItems?.contains(e.key) ?? false
                            ? c.text3
                            : c.text,
                      ),
                    ),
                  ),
              ],
              onChanged: onChanged,
            ),
          ),
        ),
        const SizedBox(height: 14),
      ],
    );
  }
}

/// 路径/单行输入框（.input-box），可带尾部「选取」按钮。
/// 自持有控制器：外部值变化（如选取目录回填）时同步，内部编辑不因重建丢光标。
class InputBoxField extends StatefulWidget {
  final String? label;
  final String text;
  final bool mono;
  final String pickLabel;
  final VoidCallback? onPick;
  final ValueChanged<String>? onChanged;
  final String? hint;

  const InputBoxField({
    super.key,
    this.label,
    this.text = '',
    this.mono = false,
    this.pickLabel = '',
    this.onPick,
    this.onChanged,
    this.hint,
  });

  @override
  State<InputBoxField> createState() => _InputBoxFieldState();
}

class _InputBoxFieldState extends State<InputBoxField> {
  late final TextEditingController _controller =
      TextEditingController(text: widget.text);
  String _lastExternal = '';

  @override
  void initState() {
    super.initState();
    _lastExternal = widget.text;
  }

  @override
  void didUpdateWidget(covariant InputBoxField oldWidget) {
    super.didUpdateWidget(oldWidget);
    // 仅当外部值确实变化（如目录选取器回填）且不等于当前编辑内容时覆盖
    if (widget.text != _lastExternal && widget.text != _controller.text) {
      _controller.text = widget.text;
      _controller.selection =
          TextSelection.collapsed(offset: _controller.text.length);
    }
    _lastExternal = widget.text;
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (widget.label != null) ...[
          FieldLabel(widget.label!),
          const SizedBox(height: 6),
        ],
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
          child: Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _controller,
                  onChanged: widget.onChanged,
                  maxLines: 1,
                  style: widget.mono
                      ? TextStyle(fontSize: 11, color: c.text, fontFamily: 'monospace')
                      : TextStyle(fontSize: 13, color: c.text),
                  decoration: InputDecoration(
                    isDense: true,
                    border: InputBorder.none,
                    hintText: widget.hint,
                    hintStyle: TextStyle(fontSize: 12, color: c.text3),
                  ),
                ),
              ),
              if (widget.onPick != null && widget.pickLabel.isNotEmpty)
                GestureDetector(
                  onTap: widget.onPick,
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(8, 6, 0, 6),
                    child: Text(widget.pickLabel,
                        style: TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w500,
                            color: c.accent)),
                  ),
                ),
            ],
          ),
        ),
        const SizedBox(height: 14),
      ],
    );
  }
}

/// 徽章 chip（.chip）：exif 蓝 / fn 绿 / err 红 / smb 紫 / ok 绿。
enum ChipKind { exif, fn, err, smb, ok }

class ChipLabel extends StatelessWidget {
  final String text;
  final ChipKind kind;

  const ChipLabel(this.text, {super.key, this.kind = ChipKind.exif});

  @override
  Widget build(BuildContext context) {
    final light = Theme.of(context).brightness == Brightness.light;
    final (bg, fg) = switch (kind) {
      ChipKind.exif => (
          light ? const Color(0xFFE8F2FB) : const Color(0xFF12324A),
          light ? const Color(0xFF0F4E7D) : const Color(0xFF9CCBEE)
        ),
      ChipKind.fn || ChipKind.ok => (
          light ? const Color(0xFFE9F7E9) : const Color(0xFF123B12),
          light ? const Color(0xFF0B5A0B) : const Color(0xFF9CD89C)
        ),
      ChipKind.err => (
          light ? const Color(0xFFFDECEB) : const Color(0xFF4A1512),
          light ? const Color(0xFFA4262C) : const Color(0xFFF3A69F)
        ),
      ChipKind.smb => (
          light ? const Color(0xFFF3E9F7) : const Color(0xFF33123F),
          light ? const Color(0xFF5A0B7C) : const Color(0xFFD5A5E8)
        ),
    };
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(10)),
      child: Text(text, style: TextStyle(fontSize: 10, color: fg)),
    );
  }
}

/// 分组标题（.section-title / .group-title）。
class SectionTitle extends StatelessWidget {
  final String text;
  final bool small;

  const SectionTitle(this.text, {super.key, this.small = false});

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: 16, bottom: 8, left: 4, right: 4),
      child: Align(
        alignment: Alignment.centerLeft,
        child: Text(
          small ? text : text.toUpperCase(),
          style: TextStyle(
            fontSize: small ? 11 : 12,
            fontWeight: FontWeight.w600,
            letterSpacing: small ? 0.3 : 0.5,
            color: c.text3,
          ),
        ),
      ),
    );
  }
}

/// 主/次/危险按钮（.btn.primary/.secondary/.danger，min-height 48）。
class MoButton extends StatelessWidget {
  final String text;
  final MoButtonStyle style;
  final VoidCallback? onPressed;
  final IconData? icon;

  const MoButton(this.text,
      {super.key, this.style = MoButtonStyle.primary, this.onPressed, this.icon});

  const MoButton.secondary(this.text, {super.key, this.onPressed, this.icon})
      : style = MoButtonStyle.secondary;

  const MoButton.danger(this.text, {super.key, this.onPressed, this.icon})
      : style = MoButtonStyle.danger;

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    final (bg, fg, border) = switch (style) {
      MoButtonStyle.primary => (c.accent, Colors.white, null),
      MoButtonStyle.secondary => (
          Theme.of(context).brightness == Brightness.light
              ? const Color(0xFFF0F0F0)
              : c.rail,
          c.text,
          null
        ),
      MoButtonStyle.danger => (Colors.transparent, c.error, c.error),
    };
    return Expanded(
      child: SizedBox(
        height: 48,
        child: OutlinedButton(
          onPressed: onPressed,
          style: OutlinedButton.styleFrom(
            backgroundColor: bg,
            foregroundColor: fg,
            side: border == null
                ? BorderSide.none
                : BorderSide(color: border),
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
            padding: const EdgeInsets.symmetric(horizontal: 20),
          ).copyWith(
            overlayColor: WidgetStateProperty.resolveWith((states) =>
                states.contains(WidgetState.pressed)
                    ? (style == MoButtonStyle.primary
                        ? c.accentHover
                        : (border ?? Colors.grey).withOpacity(0.1))
                    : null),
          ),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            mainAxisSize: MainAxisSize.min,
            children: [
              if (icon != null) ...[
                Icon(icon, size: 16),
                const SizedBox(width: 6),
              ],
              Flexible(
                child: Text(text,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                        fontSize: 14, fontWeight: FontWeight.w500)),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

enum MoButtonStyle { primary, secondary, danger }

/// 小号按钮（.btn.sm，40 高，不撑满）。放在 Row 中使用。
class MoSmallButton extends StatelessWidget {
  final String text;
  final VoidCallback? onPressed;

  const MoSmallButton(this.text, {super.key, this.onPressed});

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return SizedBox(
      height: 36,
      child: OutlinedButton(
        onPressed: onPressed,
        style: OutlinedButton.styleFrom(
          backgroundColor: Theme.of(context).brightness == Brightness.light
              ? const Color(0xFFF0F0F0)
              : c.rail,
          foregroundColor: c.text,
          side: BorderSide.none,
          padding: const EdgeInsets.symmetric(horizontal: 14),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
        ),
        child: Text(text,
            style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w500)),
      ),
    );
  }
}

/// 底部固定操作栏（.bottom-bar）。
class BottomBar extends StatelessWidget {
  final List<Widget> children;

  const BottomBar({super.key, required this.children});

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Container(
      decoration: BoxDecoration(
        color: c.card,
        border: Border(top: BorderSide(color: c.cardBorder)),
      ),
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
      child: Row(children: [
        for (var i = 0; i < children.length; i++) ...[
          if (i > 0) const SizedBox(width: 8),
          children[i],
        ]
      ]),
    );
  }
}

/// 进度条（.progress）：determinate 或 indeterminate。
class MoProgressBar extends StatelessWidget {
  /// 0~100；为 null 时显示不定态动画。
  final double? percent;

  const MoProgressBar({super.key, this.percent});

  @override
  Widget build(BuildContext context) {
    final c = AppColors.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(3),
        child: SizedBox(
          height: 6,
          child: percent == null
              ? LinearProgressIndicator(
                  backgroundColor: colors(context).track,
                  valueColor: AlwaysStoppedAnimation(c.accent),
                )
              : LinearProgressIndicator(
                  value: (percent! / 100).clamp(0.0, 1.0),
                  backgroundColor: colors(context).track,
                  valueColor: AlwaysStoppedAnimation(c.accent),
                ),
        ),
      ),
    );
  }

  static ({Color track}) colors(BuildContext context) => (
        track: Theme.of(context).brightness == Brightness.light
            ? const Color(0xFFE8E8E8)
            : const Color(0xFF3D3D3D),
      );
}
