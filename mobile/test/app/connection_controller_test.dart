import 'dart:typed_data';

import 'package:ctrl_mobile/app/app_connection_phase.dart';
import 'package:ctrl_mobile/app/connection_controller.dart';
import 'package:ctrl_mobile/protocol/auth_denied_payload.dart';
import 'package:ctrl_mobile/protocol/auth_ok_payload.dart';
import 'package:ctrl_mobile/protocol/frame.dart';
import 'package:ctrl_mobile/protocol/frame_builder.dart';
import 'package:ctrl_mobile/protocol/message_types.dart';
import 'package:ctrl_mobile/protocol/welcome_payload.dart';
import 'package:ctrl_mobile/session/token_store.dart';
import 'package:flutter_test/flutter_test.dart';

import '../support/fake_app_transport.dart';

const String _dev = 'ctrl-test-device';
final Uint8List _sessionId = Uint8List.fromList(List.generate(16, (i) => i));
final Uint8List _challenge =
    Uint8List.fromList(List.generate(32, (i) => (i * 3) & 0xFF));
final Uint8List _newToken = Uint8List.fromList(List.generate(32, (i) => 0xF0 ^ i));

WelcomePayload _welcome() => WelcomePayload(
      serverName: 'CTRL-PC',
      effectiveMajor: 1,
      effectiveMinor: 0,
      minSupportedMajor: 1,
      sessionId: _sessionId,
      authRequired: true,
      challenge: _challenge,
    );

AuthOkPayload _authOk({Uint8List? newToken}) => AuthOkPayload(
      result: authOkResultOk,
      sessionId: _sessionId,
      serverCapabilities: 0x7,
      newToken: newToken ?? Uint8List(0),
    );

Future<void> _settle() async {
  await Future<void>.delayed(Duration.zero);
  await Future<void>.delayed(Duration.zero);
}

ProtocolFrame _frame(int type, Uint8List payload, int seq) =>
    FrameCodec.decode(FrameBuilder.build(
        messageType: type,
        payload: payload,
        sequence: seq,
        mustUnderstand: true));

void main() {
  test('initial phase is disconnected and no session exists', () async {
    final controller = ConnectionController(tokenStore: InMemoryTokenStore());
    expect(controller.phase, AppConnectionPhase.disconnected);
    expect(controller.lastError, isNull);
    expect(await controller.hasStoredToken(_dev), isFalse);
  });

  test('pairing without stored token requires a pairing code', () async {
    final controller = ConnectionController(tokenStore: InMemoryTokenStore())
      ..transportFactory = (host, port) async => FakeAppTransport();
    await controller.connect(
        host: '127.0.0.1', port: 42123, deviceId: _dev, pairingCode: '');
    expect(controller.phase, AppConnectionPhase.error);
    expect(controller.lastError, contains('Pairing code required'));
  });

  test('pairing handshake reaches connected and stores the new token',
      () async {
    final store = InMemoryTokenStore();
    final fake = FakeAppTransport();
    final controller = ConnectionController(tokenStore: store)
      ..transportFactory = (host, port) async => fake;

    await controller.connect(
        host: '127.0.0.1', port: 42123, deviceId: _dev, pairingCode: '123456');
    await controller.waitForIdleForTest();
    expect(controller.phase, AppConnectionPhase.pairing);
    expect(fake.sentFrames.first.messageType, MessageType.hello);

    fake.emit(_frame(MessageType.welcome, WelcomePayloadCodec.encode(_welcome()), 0));
    await _settle();
    expect(controller.phase, AppConnectionPhase.pairing);
    expect(fake.sentFrames[1].messageType, MessageType.auth);

    fake.emit(_frame(MessageType.authOk, AuthOkPayloadCodec.encode(_authOk(newToken: _newToken)), 1));
    await _settle();

    expect(controller.phase, AppConnectionPhase.connected);
    expect(await store.load(_dev), orderedEquals(_newToken));
  });

  test('AUTH_DENIED during pairing surfaces a sanitized error and no token',
      () async {
    final store = InMemoryTokenStore();
    final fake = FakeAppTransport();
    final controller = ConnectionController(tokenStore: store)
      ..transportFactory = (host, port) async => fake;

    await controller.connect(
        host: '127.0.0.1', port: 42123, deviceId: _dev, pairingCode: 'wrong');
    fake.emit(_frame(MessageType.welcome, WelcomePayloadCodec.encode(_welcome()), 0));
    await _settle();
    fake.emit(_frame(
        MessageType.authDenied,
        AuthDeniedPayloadCodec.encode(
            AuthDeniedPayload(reason: authDeniedReasonBadCredential, message: '')),
        1));
    await _settle();

    expect(controller.phase, AppConnectionPhase.error);
    expect(controller.lastError, contains('Pairing rejected'));
    expect(await store.load(_dev), isNull);
  });

  test('unexpected drop maps to reconnecting; reset returns to disconnected',
      () async {
    final fake = FakeAppTransport();
    final controller = ConnectionController(tokenStore: InMemoryTokenStore())
      ..transportFactory = (host, port) async => fake;
    await controller.connect(
        host: '127.0.0.1', port: 42123, deviceId: _dev, pairingCode: '123456');
    fake.emit(_frame(MessageType.welcome, WelcomePayloadCodec.encode(_welcome()), 0));
    await _settle();
    fake.emitDisconnected('connection reset');
    await _settle();
    expect(controller.phase, AppConnectionPhase.reconnecting);

    await controller.resetAfterFailure();
    expect(controller.phase, AppConnectionPhase.disconnected);
  });
}
