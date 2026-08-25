import 'package:ctrl_mobile/input/touchpad_controller.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  late List<TouchpadAction> actions;
  late TouchpadController controller;

  setUp(() {
    actions = [];
    controller = TouchpadController(onAction: actions.add);
  });

  group('movement', () {
    test('single-finger drag emits clamped velocity then zero on lift',
        () async {
      controller.pointerDown(1, 100, 100, 0);
      // 80px in 50ms = 1600 px/s → fraction 2.0, clamped to 1.0.
      controller.pointerMove(1, 180, 100, 50);
      expect(actions.where((a) => a.type == TouchpadActionType.moveVelocity),
          isNotEmpty);
      final last =
          actions.lastWhere((a) => a.type == TouchpadActionType.moveVelocity);
      expect(last.vx, 1.0); // rightward at/above full speed
      expect(last.vy, 0.0);

      controller.pointerUp(1, 60);
      final zeros = actions
          .where((a) => a.type == TouchpadActionType.moveVelocity)
          .where((a) => a.vx == 0 && a.vy == 0);
      expect(zeros, isNotEmpty,
          reason: 'finger lift must zero the velocity');
    });

    test('slow movement yields sub-unit velocity', () {
      controller.pointerDown(1, 100, 100, 0);
      // Cross the drag-hold threshold first; 8px in 100ms = 80 px/s → 0.1
      // fraction before smoothing.
      controller.pointerMove(1, 108, 100, 350);
      final move = actions
          .lastWhere((a) => a.type == TouchpadActionType.moveVelocity);
      expect(move.vx, greaterThan(0));
      expect(move.vx, lessThan(0.5));
    });
  });

  group('taps', () {
    test('quick single-finger tap emits primary down+up', () {
      controller.pointerDown(1, 100, 100, 0);
      controller.pointerUp(1, 120);
      final presses = actions
          .where((a) => a.type == TouchpadActionType.button)
          .toList();
      expect(presses.length, 2);
      expect(presses[0].buttonId, 'mouse:left');
      expect(presses[0].down, isTrue);
      expect(presses[1].buttonId, 'mouse:left');
      expect(presses[1].down, isFalse);
    });

    test('long stationary press becomes drag: button held while moving',
        () {
      controller.pointerDown(1, 100, 100, 0);
      // Hold past dragMinHoldMs without moving.
      controller.pointerMove(1, 105, 100, 400);
      final held = actions.firstWhere(
          (a) => a.type == TouchpadActionType.button && a.down);
      expect(held.buttonId, 'mouse:left');

      actions.clear();
      controller.pointerMove(1, 200, 120, 430);
      expect(
          actions.any((a) => a.type == TouchpadActionType.moveVelocity),
          isTrue,
          reason: 'drag movement still moves the cursor');

      controller.pointerUp(1, 450);
      expect(
          actions.lastWhere((a) => a.type == TouchpadActionType.button).down,
          isFalse);
    });
  });

  group('two-finger scroll and secondary tap', () {
    test('second finger converts to scroll; wheel axis emitted', () {
      controller.pointerDown(1, 100, 100, 0);
      controller.pointerMove(1, 101, 101, 20);
      controller.pointerDown(2, 150, 150, 25);
      // Fast upward two-finger motion: -300px in 30ms both fingers.
      controller.pointerMove(2, 148, 60, 55);
      final wheels = actions
          .where((a) => a.type == TouchpadActionType.wheelVelocity)
          .toList();
      expect(wheels, isNotEmpty, reason: 'fast vertical two-finger scrolls');
      expect(wheels.last.wheelUp, isTrue);
    });

    test('two quick simultaneous finger lifts emit secondary click', () {
      controller.pointerDown(1, 100, 100, 0);
      controller.pointerDown(2, 130, 130, 10); // enters scroll mode
      controller.pointerUp(1, 60); // first lift; second still down
      controller.pointerUp(2, 70); // final lift within tap window
      final rights = actions
          .where((a) => a.buttonId == 'mouse:right')
          .toList();
      expect(rights.length, 2);
      expect(rights.first.down, isTrue);
      expect(rights.last.down, isFalse);
    });
  });

  group('cancellation safety', () {
    test('cancel during drag releases button and zeroes velocity', () {
      controller.pointerDown(1, 100, 100, 0);
      controller.pointerMove(1, 104, 100, 400); // enters drag
      expect(
          actions.any((a) =>
              a.type == TouchpadActionType.button &&
              a.down &&
              a.buttonId == 'mouse:left'),
          isTrue);

      actions.clear();
      controller.cancel();
      expect(actions.any((a) => a.type == TouchpadActionType.button), isTrue,
          reason: 'held drag button must be released on cancel');
      final zeros = actions.where((a) =>
          a.type == TouchpadActionType.moveVelocity &&
          a.vx == 0 &&
          a.vy == 0);
      expect(zeros, isNotEmpty,
          reason: 'cancel must emit a neutralizing zero velocity');
      expect(controller.isIdle, isTrue);
    });

    test('cancel during scroll stops wheel emission', () {
      controller.pointerDown(1, 100, 100, 0);
      controller.pointerDown(2, 140, 140, 10);
      controller.cancel();
      actions.clear();
      controller.pointerMove(2, 141, 40, 500);
      expect(actions.where((a) => a.type == TouchpadActionType.wheelVelocity),
          isEmpty,
          reason: 'after cancel, stale pointer samples are ignored');
    });
  });
}
