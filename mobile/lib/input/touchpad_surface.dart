import 'package:flutter/material.dart';

import 'touchpad_controller.dart';

/// Production touchpad surface (M3.0 Phase 2): converts raw pointer gestures
/// into semantic [TouchpadAction]s via [TouchpadController] and forwards them
/// to [onAction]. Gesture interpretation is isolated in the pure controller;
/// this widget only owns pointer plumbing and visuals.
///
/// Safety: any pointer cancel, or disposal mid-gesture, runs
/// `controller.cancel()` so buttons/velocity can never stay stuck.
class TouchpadSurface extends StatefulWidget {
  const TouchpadSurface({
    super.key,
    required this.onAction,
    this.config,
  });

  final void Function(TouchpadAction action) onAction;
  final TouchpadConfig? config;

  @override
  State<TouchpadSurface> createState() => _TouchpadSurfaceState();
}

class _TouchpadSurfaceState extends State<TouchpadSurface> {
  late final TouchpadController _controller =
      TouchpadController(config: widget.config ?? const TouchpadConfig())
        ..onAction = widget.onAction;

  @override
  void didUpdateWidget(covariant TouchpadSurface oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!identical(oldWidget.onAction, widget.onAction)) {
      _controller.onAction = widget.onAction;
    }
  }

  @override
  void dispose() {
    _controller.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AspectRatio(
      aspectRatio: 16 / 10,
      child: Listener(
        behavior: HitTestBehavior.opaque,
        onPointerDown: (e) => _controller
            .pointerDown(e.pointer, e.localPosition.dx, e.localPosition.dy,
                e.timeStamp.inMilliseconds),
        onPointerMove: (e) => _controller
            .pointerMove(e.pointer, e.localPosition.dx, e.localPosition.dy,
                e.timeStamp.inMilliseconds),
        onPointerUp: (e) => _controller
            .pointerUp(e.pointer, e.timeStamp.inMilliseconds),
        onPointerCancel: (_) => _controller.cancel(),
        child: Container(
          decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.surfaceContainerHighest,
            borderRadius: BorderRadius.circular(12),
            border: Border.all(
                color: Theme.of(context).colorScheme.outlineVariant),
          ),
          alignment: Alignment.center,
          child: Text('Touchpad',
              style: TextStyle(color:
                  Theme.of(context).colorScheme.outline)),
        ),
      ),
    );
  }
}
