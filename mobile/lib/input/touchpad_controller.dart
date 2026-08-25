/// Pure gesture interpretation for the CTRL touchpad surface (M3.0 Phase 2).
///
/// Converts raw pointer samples into semantic touchpad actions. No Flutter
/// imports — fully deterministic and unit-testable. Values preserve protocol
/// ranges: stick velocities are clamped to -1..1, wheel speeds to 0..1
/// (docs/protocol.md §9). The desktop owns the actual cursor/wheel output
/// semantics (M2.2); this class only produces protocol-valid input events.
library;

import 'dart:math';

/// Semantic output of the gesture interpreter.
enum TouchpadActionType { moveVelocity, wheelVelocity, button }

class TouchpadAction {
  const TouchpadAction.move(this.vx, this.vy)
      : type = TouchpadActionType.moveVelocity,
        buttonId = null,
        down = false,
        wheelUp = false;

  const TouchpadAction.wheel({required bool up, required double speed})
      : type = TouchpadActionType.wheelVelocity,
        vx = 0,
        vy = 0,
        buttonId = null,
        down = false,
        wheelUp = up;

  const TouchpadAction.button(this.buttonId, this.down)
      : type = TouchpadActionType.button,
        vx = 0,
        vy = 0,
        wheelUp = false;

  final TouchpadActionType type;
  final double vx;
  final double vy;
  final String? buttonId; // 'mouse:left' | 'mouse:right'
  final bool down;
  final bool wheelUp;
}

/// Tunable thresholds (documented defaults; unit tests pin behavior).
class TouchpadConfig {  const TouchpadConfig({
    this.tapMaxDurationMs = 250,
    this.tapSlopPx = 12,
    this.dragMinHoldMs = 300,
    this.pointerSpeedPxPerSecond = 800,
    this.scrollPxPerSecondAtFullSpeed = 700,
    this.velocitySmoothing = 0.5,
  });

  /// Maximum press duration still counted as a tap.
  final int tapMaxDurationMs;

  /// Maximum travel (px) still counted as a tap / pre-drag hold.
  final double tapSlopPx;

  /// Hold duration that converts a stationary press into a drag.
  final int dragMinHoldMs;

  /// Finger speed (px/s) that equals full cursor deflection.
  final double pointerSpeedPxPerSecond;

  /// Finger speed (px/s) that equals full wheel rate.
  final double scrollPxPerSecondAtFullSpeed;

  /// Exponential smoothing factor for movement velocity (0..1).
  final double velocitySmoothing;
}

/// Interprets multi-pointer samples into [TouchpadAction]s:
///   one finger drag      → relative cursor velocity (-1..1)
///   one finger quick tap → primary click
///   long stationary hold → drag mode (primary held while moving)
///   two-finger drag      → wheel scroll (direction by vertical motion)
///   two-finger quick tap → secondary click
///
/// Any cancellation resets all derived state and emits neutralizing actions
/// (velocity zero, held buttons released) so input can never stay stuck.
enum _Mode { idle, maybeMoveOrTap, moving, drag, scroll }

class TouchpadController {
  TouchpadController({this.config = const TouchpadConfig(), this.onAction});

  final TouchpadConfig config;

  /// Action sink; assignable so the surface widget can re-bind on update.
  void Function(TouchpadAction action)? onAction;

  _Mode _mode = _Mode.idle;
  final Map<int, (double x, double y, int downMs)> _pointers = {};
  int? _primaryId;
  double _originX = 0, _originY = 0;
  int _originMs = 0;
  int _lastSampleMs = 0;
  double _smoothedSpeedFraction = 0;
  bool _leftHeld = false;
  int _scrollStartMs = 0;
  double _scrollTravel = 0;
  bool _firstMoveSample = true;

  bool get isIdle => _mode == _Mode.idle;

  void _emit(TouchpadAction a) => onAction?.call(a);

  void _zeroVelocity() => _emit(const TouchpadAction.move(0, 0));

  void pointerDown(int pointerId, double x, double y, int timeMs) {
    if (_pointers.containsKey(pointerId)) {
      return;
    }
    switch (_pointers.length) {
      case 0:
        _pointers[pointerId] = (x, y, timeMs);
        _primaryId = pointerId;
        _originX = x;
        _originY = y;
        _originMs = timeMs;
        _lastSampleMs = timeMs;
        _smoothedSpeedFraction = 0;
        _mode = _Mode.maybeMoveOrTap;
        break;
      case 1:
        // Second finger: enter two-finger scroll mode. A quick two-finger
        // lift (tap) is detected on pointer-up via scroll duration/travel.
        _cancelPendingPress();
        _pointers[pointerId] = (x, y, timeMs);
        _scrollStartMs = timeMs;
        _scrollTravel = 0;
        _lastSampleMs = timeMs;
        _mode = _Mode.scroll;
        break;
      default:
        // Three+ fingers are outside M3.0 scope; ignore extras.
        break;
    }
  }

  void pointerMove(int pointerId, double x, double y, int timeMs) {
    final previous = _pointers[pointerId];
    if (previous == null) {
      return;
    }
    final dx = x - previous.$1;
    final dy = y - previous.$2;
    final dtMs = (timeMs - _lastSampleMs).clamp(1, 200);
    _pointers[pointerId] = (x, y, previous.$3);
    _lastSampleMs = timeMs;

    switch (_mode) {
      case _Mode.maybeMoveOrTap:
        if (_travelFromOrigin(x, y) > config.tapSlopPx ||
            timeMs - _originMs >= config.dragMinHoldMs) {
          // Travel confirms movement; exceeding the hold window confirms drag.
          _beginMoving(timeMs);
          _firstMoveSample = true;
          _emitMoveFor(dx, dy, dtMs);
        }
        break;
      case _Mode.moving:
        _emitMoveFor(dx, dy, dtMs);
        break;
      case _Mode.drag:
        _emitMoveFor(dx, dy, dtMs);
        break;
      case _Mode.scroll:
        final dyAvg = _otherPointerDeltaY(pointerId, dy);
        final speed =
            clamp01((dyAbs(dyAvg) / dtMs * 1000) / config.scrollPxPerSecondAtFullSpeed);
        if (speed > 0.05) {
          _emit(TouchpadAction.wheel(up: dyAvg < 0, speed: speed));
        }
        break;
      case _Mode.idle:
        break;
    }
  }

  void pointerUp(int pointerId, int timeMs) {
    final removed = _pointers.remove(pointerId);
    if (removed == null) {
      return;
    }

    switch (_mode) {
      case _Mode.maybeMoveOrTap:
        final heldMs = timeMs - _originMs;
        final wasPrimary = pointerId == _primaryId;
        final traveled = sqrt(
            pow2(removed.$1 - _originX) + pow2(removed.$2 - _originY));
        if (wasPrimary &&
            _pointers.isEmpty &&
            heldMs <= config.tapMaxDurationMs &&
            traveled <= config.tapSlopPx) {
          _emit(const TouchpadAction.button('mouse:left', true));
          _emit(const TouchpadAction.button('mouse:left', false));
        }
        if (_pointers.isEmpty) {
          _reset();
        } else {
          _promoteRemaining();
        }
        break;
      case _Mode.moving:
        if (_pointers.isEmpty) {
          _zeroVelocity();
          _reset();
        } else {
          _promoteRemaining();
        }
        break;
      case _Mode.drag:
        if (pointerId == _primaryId) {
          _releaseDragButton();
          if (_pointers.isEmpty) {
            _reset();
          } else {
            _promoteRemaining();
          }
        }
        break;
      case _Mode.scroll:
        if (_pointers.isEmpty) {
          // A very short, low-travel two-finger contact is a right click.
          final duration = timeMs - _scrollStartMs;
          if (duration <= config.tapMaxDurationMs &&
              _scrollTravel <= config.tapSlopPx * 2) {
            _emit(const TouchpadAction.button('mouse:right', true));
            _emit(const TouchpadAction.button('mouse:right', false));
          }
          _reset();
        } else {
          _promoteRemaining();
        }
        break;
      case _Mode.idle:
        break;
    }
    if (_pointers.isEmpty && _mode != _Mode.idle) {
      _reset();
    }
  }

  /// Gesture cancelled (widget disposed, app lifecycle, connection lost):
  /// releases everything so no button or velocity can stay stuck.
  void cancel() {
    if (_leftHeld) {
      _releaseDragButton();
    }
    if (_mode == _Mode.moving || _mode == _Mode.drag) {
      _zeroVelocity();
    }
    _reset();
  }

  // --- internals -----------------------------------------------------------

  void _beginMoving(int timeMs) {
    final heldLongEnough = timeMs - _originMs >= config.dragMinHoldMs &&
        _travelFromOrigin(
                _pointers[_primaryId]?.$1 ?? _originX,
                _pointers[_primaryId]?.$2 ?? _originY) <=
            config.tapSlopPx;
    if (heldLongEnough) {
      _mode = _Mode.drag;
      _leftHeld = true;
      _emit(const TouchpadAction.button('mouse:left', true));
    } else {
      _mode = _Mode.moving;
    }
  }

  void _emitMoveFor(double dx, double dy, int dtMs) {
    final dist = sqrt(dx * dx + dy * dy);
    if (dist < 0.0001) {
      return;
    }
    final speedFraction = clamp01(
        (dist / dtMs * 1000) / config.pointerSpeedPxPerSecond);
    if (_firstMoveSample) {
      // First sample of a gesture defines its baseline speed directly.
      _firstMoveSample = false;
      _smoothedSpeedFraction = speedFraction;
    } else {
      _smoothedSpeedFraction = _smoothedSpeedFraction *
              (1 - config.velocitySmoothing) +
          speedFraction * config.velocitySmoothing;
    }
    final inv = 1 / dist;
    _emit(TouchpadAction.move(
        clamp11(dx * inv * _smoothedSpeedFraction),
        clamp11(dy * inv * _smoothedSpeedFraction)));
  }

  void _releaseDragButton() {
    if (_leftHeld) {
      _leftHeld = false;
      _emit(const TouchpadAction.button('mouse:left', false));
    }
  }

  void _cancelPendingPress() {
    // Entering scroll discards any unconfirmed tap/drag; a confirmed drag
    // releases its held primary button so scrolling never drags.
    if (_leftHeld) {
      _releaseDragButton();
    }
    if (_mode == _Mode.moving) {
      _zeroVelocity();
    }
  }

  double _travelFromOrigin(double x, double y) =>
      sqrt(pow2(x - _originX) + pow2(y - _originY));

  double _otherPointerDeltaY(int movedPointerId, double movedDy) {
    // Two-pointer scroll uses the average vertical motion; the unmoved other
    // pointer contributes whatever we last saw — approximated by doubling the
    // moved finger's delta when both are present.
    return _pointers.length >= 2 ? movedDy * 2 : movedDy;
  }

  void _promoteRemaining() {
    if (_pointers.isEmpty) {
      _reset();
      return;
    }
    final entry = _pointers.entries.first;
    _primaryId = entry.key;
    _originX = entry.value.$1;
    _originY = entry.value.$2;
    _originMs = entry.value.$3;
    switch (_mode) {
      case _Mode.scroll:
        break; // stays scroll until all fingers lift
      default:
        _mode = _Mode.idle;
    }
  }

  void _reset() {
    _mode = _Mode.idle;
    _pointers.clear();
    _primaryId = null;
    _smoothedSpeedFraction = 0;
    _firstMoveSample = true;
    _leftHeld = false;
  }
}

double pow2(double v) => v * v;
double clamp11(double v) => v.clamp(-1.0, 1.0).toDouble();
double clamp01(double v) => v.clamp(0.0, 1.0).toDouble();
double dyAbs(double v) => v.abs();
