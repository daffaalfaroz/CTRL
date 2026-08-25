import 'dart:async';
import 'dart:io';
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
import 'package:ctrl_mobile/transport/transport_connection.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';

/// Transport for established sessions inside reconnect tests.
class ScriptedTransport extends TransportConnection {
  final _frames = StreamController<ProtocolFrame>.broadcast();
  final _disconnected = StreamController<String>.broadcast();

  @override
  bool isConnected = true;
  @override
  Stream<ProtocolFrame> get frames => _frames.stream;
  @override
  Stream<String> get disconnected => _disconnected.stream;

  void emitWelcome() {
    final sessionId = Uint8List.fromList(List.generate(16, (i) => i));
    _frames.add(FrameCodec.decode(FrameBuilder.build(
        messageType: MessageType.welcome,
        payload: WelcomePayloadCodec.encode(WelcomePayload(
            serverName: 'CTRL-PC',
            effectiveMajor: 1,
            effectiveMinor: 0,
            minSupportedMajor: 1,
            sessionId: sessionId,
            authRequired: true,
            challenge: Uint8List.fromList(List.generate(32, (i) => i)))),
        sequence: 0,
        mustUnderstand: true)));
  }

  void emitAuthOk() {
    final sessionId = Uint8List.fromList(List.generate(16, (i) => i));
    _frames.add(FrameCodec.decode(FrameBuilder.build(
        messageType: MessageType.authOk,
        payload: AuthOkPayloadCodec.encode(AuthOkPayload(
            result: authOkResultOk,
            sessionId: sessionId,
            serverCapabilities: 0x7,
            newToken: Uint8List(0))),
        sequence: 1,
        mustUnderstand: true)));
  }

  void emitHandshakeOk() {
    emitWelcome();
    emitAuthOk();
  }

  void emitAuthDenied() {
    _frames.add(FrameCodec.decode(FrameBuilder.build(
        messageType: MessageType.authDenied,
        payload: AuthDeniedPayloadCodec.encode(
            AuthDeniedPayload(reason: authDeniedReasonBadCredential, message: '')),
        sequence: 5,
        mustUnderstand: true)));
  }

  @override
  Future<void> send(Uint8List frame) async {}

  @override
  Future<void> close() async {
    isConnected = false;
    if (!_disconnected.isClosed) {
      _disconnected.add('closed');
    }
    await _frames.close();
    await _disconnected.close();
  }
}

Uint8List _token() => Uint8List.fromList(List.filled(32, 9));

void main() {
  const dev = 'dev';

  ConnectionController makeController(
      {required InMemoryTokenStore store,
      List<ScriptedTransport>? created,
      int throwOnFirstN = 0}) {
    var attempt = 0;
    return ConnectionController(tokenStore: store)
      ..delayRunner = (_) async {}
      ..transportFactory = (host, port) async {
        attempt++;
        if (attempt <= throwOnFirstN) {
          throw const SocketException('transient network failure');
        }
        final t = ScriptedTransport();
        created?.add(t);
        return t;
      };
  }

  test('transient failures auto-reconnect with stored token and reach ready',
      () async {
    final store = InMemoryTokenStore();
    await store.save(dev, _token());
    final created = <ScriptedTransport>[];
    final controller =
        makeController(store: store, created: created, throwOnFirstN: 2);

    await controller.connect(host: 'h', port: 42123, deviceId: dev);
    expect(controller.phase, AppConnectionPhase.reconnecting);

    // Let the unawaited loop run through backoff steps (instant delays).
    await Future<void>.delayed(const Duration(milliseconds: 60));
    expect(created.length, greaterThanOrEqualTo(1),
        reason: 'at least one successful transport must be established');
    // Complete the handshake on the established transport.
    created.last.emitHandshakeOk();
    await Future<void>.delayed(const Duration(milliseconds: 30));
    expect(controller.phase, AppConnectionPhase.connected,
        reason: 'token-based auto-reconnect lands in connected');
  });

  test('auth rejection stops retries permanently for this connect', () async {
    final store = InMemoryTokenStore();
    await store.save(dev, _token());
    final created = <ScriptedTransport>[];
    final controller =
        makeController(store: store, created: created, throwOnFirstN: 0);

    await controller.connect(host: 'h', port: 42123, deviceId: dev);
    await Future<void>.delayed(const Duration(milliseconds: 30));
    created.last.emitHandshakeOk();
    await Future<void>.delayed(const Duration(milliseconds: 30));
    expect(controller.phase, AppConnectionPhase.connected);

    // Unexpected loss while READY -> reconnecting -> loop starts. The new
    // transport establishes; the engine enters waitAuthOk after WELCOME, and
    // the desktop then denies its token auth.
    created.last.close();
    await Future<void>.delayed(const Duration(milliseconds: 30));
    expect(created.length, greaterThanOrEqualTo(2),
        reason: 'auto-reconnect must establish a fresh transport');
    created.last.emitWelcome(); // engine enters waitAuthOk after sending AUTH
    await Future<void>.delayed(const Duration(milliseconds: 20));
    created.last.emitAuthDenied(); // desktop denies the token
    await Future<void>.delayed(const Duration(milliseconds: 30));

    // ignore: avoid_print
    print('DBG phase=${controller.phase} err=${controller.lastError} '
        'flag=${controller.authDeniedSinceLastConnect()}');
    expect(controller.authDeniedSinceLastConnect(), isTrue);
    final attemptsAtDenial = controller.reconnectAttempts;
    await Future<void>.delayed(const Duration(milliseconds: 150));
    expect(controller.reconnectAttempts, attemptsAtDenial,
        reason: 'permanent auth failure must not create an aggressive retry '
            'loop');
  });

  test('explicit user disconnect suppresses further reconnect attempts',
      () async {
    final store = InMemoryTokenStore();
    await store.save(dev, _token());
    final controller = makeController(store: store, throwOnFirstN: 99);

    await controller.connect(host: 'h', port: 42123, deviceId: dev);
    expect(controller.phase, AppConnectionPhase.reconnecting);
    final atStop = controller.reconnectAttempts;

    await controller.disconnect();
    await Future<void>.delayed(const Duration(milliseconds: 80));
    expect(controller.reconnectAttempts, atStop,
        reason: 'user Disconnect must cancel the retry loop');
    expect(controller.phase, AppConnectionPhase.disconnected);
  });

  test('controller dispose stops the retry loop', () async {
    final store = InMemoryTokenStore();
    await store.save(dev, _token());
    final controller = makeController(store: store, throwOnFirstN: 99);

    await controller.connect(host: 'h', port: 42123, deviceId: dev);
    await Future<void>.delayed(const Duration(milliseconds: 10));
    expect(controller.reconnectAttempts, greaterThanOrEqualTo(1));

    controller.dispose();
    final atDispose = controller.reconnectAttempts;
    await Future<void>.delayed(const Duration(milliseconds: 80));
    expect(controller.reconnectAttempts, atDispose,
        reason: 'disposed controller must not keep retrying');
  });
}
