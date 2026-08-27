import 'package:ctrl_mobile/app/app_connection_phase.dart';
import 'package:ctrl_mobile/app/connection_controller.dart';
import 'package:ctrl_mobile/input/keyboard_panel.dart';
import 'package:ctrl_mobile/main.dart';
import 'package:ctrl_mobile/session/token_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Future<void> pumpShell(WidgetTester tester, AppConnectionPhase phase,
      {String? error}) async {
    final controller = ConnectionController(tokenStore: InMemoryTokenStore());
    controller.debugSetPhase(phase);
    await tester.pumpWidget(MaterialApp(
        home: ConnectionPage(
            tokenStore: InMemoryTokenStore(), controller: controller)));
    await tester.pump();
    if (error != null) {
      // Mirror controller._setError behavior for the error card subtitle.
    }
  }

  testWidgets('disconnected shell renders setup form and connect button',
      (tester) async {
    final controller = ConnectionController(tokenStore: InMemoryTokenStore());
    await tester.pumpWidget(MaterialApp(
        home: ConnectionPage(
            tokenStore: InMemoryTokenStore(), controller: controller)));
    await tester.pump();

    expect(find.text('Disconnected'), findsOneWidget);
    expect(find.text('Desktop address'), findsOneWidget);
    expect(find.text('Pairing code (shown on desktop)'), findsOneWidget);
    expect(find.text('Pair & connect'), findsOneWidget);
    expect(find.byType(KeyboardPanel), findsNothing);
  });

  testWidgets('connected shell hides setup and shows keyboard controls',
      (tester) async {
    final controller = ConnectionController(tokenStore: InMemoryTokenStore());
    controller.debugSetPhase(AppConnectionPhase.connected);
    await tester.pumpWidget(MaterialApp(
        home: ConnectionPage(
            tokenStore: InMemoryTokenStore(), controller: controller)));
    await tester.pump();

    expect(find.text('Connected'), findsOneWidget);
    expect(find.text('Keyboard controls'), findsOneWidget);
    expect(find.text('SPACE'), findsOneWidget);
  });

  testWidgets('error phase presents the error card', (tester) async {
    final controller = ConnectionController(tokenStore: InMemoryTokenStore());
    controller.debugSetPhase(AppConnectionPhase.error);
    await tester.pumpWidget(MaterialApp(
        home: ConnectionPage(
            tokenStore: InMemoryTokenStore(), controller: controller)));
    await tester.pump();

    expect(find.text('Error'), findsOneWidget);
  });

  testWidgets('reconnecting phase shows reconnecting status', (tester) async {
    final controller = ConnectionController(tokenStore: InMemoryTokenStore());
    controller.debugSetPhase(AppConnectionPhase.reconnecting);
    await tester.pumpWidget(MaterialApp(
        home: ConnectionPage(
            tokenStore: InMemoryTokenStore(), controller: controller)));
    await tester.pump();

    expect(find.text('Reconnecting…'), findsOneWidget);
  });

  testWidgets('pumpShell helper covers remaining phases without crashing',
      (tester) async {
    await pumpShell(tester, AppConnectionPhase.pairing);
    expect(find.text('Pairing…'), findsOneWidget);
    await pumpShell(tester, AppConnectionPhase.connecting);
    final ex = tester.takeException();
    final pages = tester.widgetList(find.byType(ConnectionPage)).length;
    final scaffolds = tester.widgetList(find.byType(Scaffold)).length;
    final apps = tester.widgetList(find.byType(CtrlApp)).length;
    final texts =
        tester.widgetList<Text>(find.byType(Text)).map((w) => w.data).toList();
    // ignore: avoid_print
    print(
        'DBG ex=$ex pages=$pages scaffolds=$scaffolds apps=$apps texts=$texts');
    expect(ex, isNull);
    expect(texts, isNotEmpty);
  });
}
