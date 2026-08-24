import 'package:ctrl_mobile/input/keyboard_bindings.dart';
import 'package:ctrl_mobile/input/keyboard_panel.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('keyboard bindings (CTRL convention)', () {
    test('canonical controlIds use the key:<NAME> form', () {
      expect(keyboardControlId('A'), 'key:A');
      expect(keyboardControlId('space'), 'key:SPACE');
      expect(keyboardControlId('LControl'), 'key:LCONTROL');
      expect(keyboardControlId('F5'), 'key:F5');
    });

    test('unknown names fail loudly', () {
      expect(() => keyboardControlId('COFFEE'), throwsArgumentError);
      expect(() => keyboardControlId(''), throwsArgumentError);
    });

    test('letters, digits, function keys and table names are supported', () {
      expect(isSupportedKeyName('a'), isTrue);
      expect(isSupportedKeyName('7'), isTrue);
      expect(isSupportedKeyName('F24'), isTrue);
      expect(isSupportedKeyName('DELETE'), isTrue);
      expect(isSupportedKeyName('F25'), isFalse);
      expect(isSupportedKeyName('COFFEE'), isFalse);
      expect(isSupportedKeyName('FF'), isFalse);
    });
  });

  group('KeyboardPanel', () {
    Future<List<(String, bool)>> pumpAndTap(WidgetTester tester, String label) async {
      final captured = <(String, bool)>[];
      await tester.pumpWidget(MaterialApp(
        home: Scaffold(
          body: KeyboardPanel(
            keys: const ['A', 'SPACE', 'LCONTROL'],
            onKeyEvent: (controlId, down) => captured.add((controlId, down)),
          ),
        ),
      ));
      await tester.tap(find.text(label));
      await tester.pump();
      return captured;
    }

    testWidgets('pressing a key emits down then up with canonical controlIds',
        (tester) async {
      final captured = await pumpAndTap(tester, 'A');
      expect(captured, [
        ('key:A', true),
        ('key:A', false),
      ]);
    });

    testWidgets('modifier keys emit the same canonical convention',
        (tester) async {
      final captured = await pumpAndTap(tester, 'SPACE');
      expect(captured, [
        ('key:SPACE', true),
        ('key:SPACE', false),
      ]);
    });
  });
}
