import 'package:ctrl_mobile/input/mouse_bindings.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('mouse bindings (CTRL convention, M2.2)', () {
    test('button controlIds follow the mouse:<button> form', () {
      expect(mouseLeftControlId, 'mouse:left');
      expect(mouseRightControlId, 'mouse:right');
      expect(mouseMiddleControlId, 'mouse:middle');
    });

    test('movement and wheel control ids are exposed', () {
      expect(mouseMoveControlId, 'mouse:move');
      expect(mouseWheelUpControlId, 'mouse:wheelup');
      expect(mouseWheelDownControlId, 'mouse:wheeldown');
    });

    test('exactly three buttons are supported in M2.2', () {
      expect(supportedMouseButtons.length, 3);
      expect(supportedMouseButtons.toSet().length, 3);
    });
  });
}
