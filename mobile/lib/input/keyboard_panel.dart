import 'package:flutter/material.dart';

import 'keyboard_bindings.dart';

/// Minimal on-screen keyboard control (M2.1). Renders a compact grid of named
/// keys; pressing a key reports the canonical `key:<NAME>` controlId through
/// [onKeyEvent]. The widget is transport-agnostic — the caller owns the
/// ClientSession wiring, keeping this reusable for future layouts.
class KeyboardPanel extends StatelessWidget {
  const KeyboardPanel({
    super.key,
    required this.keys,
    required this.onKeyEvent,
  });

  /// Canonical key names to render (see [isSupportedKeyName]).
  final List<String> keys;

  /// Called with `key:<NAME>` on press-down and press-up of each key.
  final void Function(String controlId, bool down) onKeyEvent;

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: 6,
      runSpacing: 6,
      children: [
        for (final keyName in keys)
          _KeyCap(
            label: keyName,
            controlId: keyboardControlId(keyName),
            onKeyEvent: onKeyEvent,
          ),
      ],
    );
  }
}

class _KeyCap extends StatefulWidget {
  const _KeyCap({
    required this.label,
    required this.controlId,
    required this.onKeyEvent,
  });

  final String label;
  final String controlId;
  final void Function(String controlId, bool down) onKeyEvent;

  @override
  State<_KeyCap> createState() => _KeyCapState();
}

class _KeyCapState extends State<_KeyCap> {
  bool _down = false;

  void _set(bool down) {
    if (_down == down) {
      return;
    }
    setState(() => _down = down);
    widget.onKeyEvent(widget.controlId, down);
  }

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTapDown: (_) => _set(true),
      onTapUp: (_) => _set(false),
      onTapCancel: () => _set(false),
      child: Container(
        width: 56,
        height: 44,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: _down ? Theme.of(context).colorScheme.primaryContainer : null,
          border: Border.all(color: Theme.of(context).colorScheme.outline),
          borderRadius: BorderRadius.circular(8),
        ),
        child: Text(widget.label),
      ),
    );
  }
}
