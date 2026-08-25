import 'package:ctrl_mobile/app/connection_controller.dart';
import 'package:ctrl_mobile/main.dart';
import 'package:ctrl_mobile/session/token_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  // M3.0 replaced the Flutter template counter with the CTRL connection
  // shell; this smoke test pins the new real entry point.
  testWidgets('CTRL app shell starts in the disconnected state', (tester) async {
    await tester.pumpWidget(MaterialApp(
      home: ConnectionPage(
        tokenStore: InMemoryTokenStore(),
        controller: ConnectionController(tokenStore: InMemoryTokenStore()),
      ),
    ));
    await tester.pump();

    expect(find.text('Disconnected'), findsOneWidget);
    expect(find.text('Pair & connect'), findsOneWidget);
    expect(find.text('Desktop address'), findsOneWidget);
  });
}
