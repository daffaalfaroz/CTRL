import 'package:ctrl_mobile/input/gamepad_bindings.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('gamepad bindings (abstract gamepad, CAP_GAMEPAD)', () {
    test('face/shoulder/system/stick-click ids follow gamepad:<name>', () {
      expect(gpA, 'gamepad:a');
      expect(gpB, 'gamepad:b');
      expect(gpX, 'gamepad:x');
      expect(gpY, 'gamepad:y');
      expect(gpLeftShoulder, 'gamepad:lb');
      expect(gpRightShoulder, 'gamepad:rb');
      expect(gpBack, 'gamepad:back');
      expect(gpStart, 'gamepad:start');
      expect(gpLeftStickClick, 'gamepad:lsclick');
      expect(gpRightStickClick, 'gamepad:rsclick');
    });

    test('analog and d-pad control ids are exposed', () {
      expect(gpLeftStick, 'gamepad:lstick');
      expect(gpRightStick, 'gamepad:rstick');
      expect(gpLeftTrigger, 'gamepad:lt');
      expect(gpRightTrigger, 'gamepad:rt');
      expect(gpDpad, 'gamepad:dpad');
    });

    test('ten buttons are supported in M2.4', () {
      expect(supportedGamepadButtons.length, 10);
      expect(supportedGamepadButtons.toSet().length, 10);
    });

    test('d-pad hat values cover center + 8 directions (§9)', () {
      const values = [
        dpadCenter,
        dpadUp,
        dpadUpRight,
        dpadRight,
        dpadDownRight,
        dpadDown,
        dpadDownLeft,
        dpadLeft,
        dpadUpLeft,
      ];
      expect(values, [0, 1, 2, 3, 4, 5, 6, 7, 8]);
    });
  });
}
