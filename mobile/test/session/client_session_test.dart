import 'dart:async';
import 'dart:typed_data';

import 'package:ctrl_mobile/protocol/ack_payload.dart';
import 'package:ctrl_mobile/protocol/auth_denied_payload.dart';
import 'package:ctrl_mobile/protocol/auth_ok_payload.dart';
import 'package:ctrl_mobile/protocol/auth_payload.dart';
import 'package:ctrl_mobile/protocol/disconnect_payload.dart';
import 'package:ctrl_mobile/protocol/error_payload.dart';
import 'package:ctrl_mobile/protocol/frame.dart';
import 'package:ctrl_mobile/protocol/hello_payload.dart';
import 'package:ctrl_mobile/protocol/input_event.dart';
import 'package:ctrl_mobile/protocol/input_reset_payload.dart';
import 'package:ctrl_mobile/protocol/input_snapshot_payload.dart';
import 'package:ctrl_mobile/protocol/message_types.dart';
import 'package:ctrl_mobile/protocol/pong_payload.dart';
import 'package:ctrl_mobile/protocol/welcome_payload.dart';
import 'package:ctrl_mobile/session/ack_tracker.dart';
import 'package:ctrl_mobile/session/authenticator.dart';
import 'package:ctrl_mobile/session/client_session.dart';
import 'package:ctrl_mobile/session/input_snapshot_provider.dart';
import 'package:ctrl_mobile/session/sequence_tracker.dart';
import 'package:ctrl_mobile/session/session_listener.dart';
import 'package:ctrl_mobile/session/session_state.dart';
import 'package:ctrl_mobile/transport/transport_connection.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final sessionId = Uint8List.fromList(
      List.generate(16, (i) => i)); // 0x00..0x0F
  final challenge =
      Uint8List.fromList(List.generate(32, (i) => 0x10 + i)); // 0x10..0x2F
  const deviceId = 'ctrl-42a8';

  WelcomePayload welcome() => WelcomePayload(
        serverName: 'CTRL-PC',
        effectiveMajor: 1,
        effectiveMinor: 0,
        minSupportedMajor: 1,
        sessionId: sessionId,
        authRequired: true,
        challenge: challenge,
      );

  AuthOkPayload authOk([Uint8List? id]) => AuthOkPayload(
        result: authOkResultOk,
        sessionId: id ?? sessionId,
        serverCapabilities: 0x00000007,
        newToken: Uint8List(0),
      );

  ClientSession newSession(FakeTransport fake, RecordingListener listener,
      {int Function()? nowMs, int ackTimeoutMs = 3000}) {
    return ClientSession(
      transport: fake,
      authenticator: EchoAuthenticator(
        credentialType: authCredentialTypeToken,
        credential: '',
        deviceId: deviceId,
      ),
      listener: listener,
      inputSnapshotProvider: _FixedSnapshotProvider(),
      nowMs: nowMs,
      ackTimeoutMs: ackTimeoutMs,
    );
  }

  group('SequenceTracker', () {
    test('outbound is continuous and inbound is monotonic modulo 2^16', () {
      final outbound = SequenceTracker();
      expect(outbound.next(), 0);
      expect(outbound.next(), 1);
      expect(outbound.next(), 2);

      final inbound = InboundSequenceTracker();
      expect(inbound.isMonotonic(0), isTrue);
      expect(inbound.isMonotonic(1), isTrue);
      expect(inbound.isMonotonic(2), isTrue);
      expect(inbound.isMonotonic(2), isFalse, reason: 'duplicate');
      expect(inbound.isMonotonic(0), isFalse, reason: 'regression');
      expect(inbound.isMonotonic(40000), isFalse, reason: 'jump over 0x7FFF');
      expect(inbound.isMonotonic(40001), isTrue);
    });
  });

  group('AckTracker', () {
    test('retries once then fails', () {
      var now = 0;
      final tracker = AckTracker(nowMs: () => now, timeoutMs: 3000);
      tracker.track(10);
      expect(tracker.pendingCount, 1);
      now = 2999;
      expect(tracker.retryExpired(), isEmpty);
      now = 3000;
      expect(tracker.retryExpired(), [10]);
      expect(tracker.retryExpired(), isEmpty, reason: 'retry exactly once');
      now = 6000;
      expect(tracker.failed(), [10]);
    });

    test('ack clears pending state', () {
      final tracker = AckTracker(nowMs: () => 0, timeoutMs: 3000);
      tracker.track(20);
      tracker.acknowledge(20);
      expect(tracker.pendingCount, 0);
      expect(tracker.isPending(20), isFalse);
    });
  });

  group('ClientSession handshake', () {
    test('HELLO(seq0) -> WELCOME -> AUTH(seq1) -> AUTH_OK -> snapshot(seq2)',
        () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);

      session.connect();
      await session.waitForIdle();
      expect(session.state, ClientSessionState.waitWelcome);
      expect(fake.sentFrames.length, 1);
      expect(fake.sentFrames[0].messageType, MessageType.hello);
      expect(fake.sentFrames[0].sequence, 0);

      fake.emit(_frame(MessageType.welcome, WelcomePayloadCodec.encode(welcome())));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.waitAuthOk);
      expect(fake.sentFrames.length, 2);
      expect(fake.sentFrames[1].messageType, MessageType.auth);
      expect(fake.sentFrames[1].sequence, 1);

      final auth = AuthPayloadCodec.decode(fake.sentFrames[1].payload);
      expect(auth.deviceId, deviceId);
      expect(auth.challengeResponse, challenge, reason: 'D4 echo challenge');

      fake.emit(_frame(MessageType.authOk, AuthOkPayloadCodec.encode(authOk())));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.ready);
      expect(fake.sentFrames.length, 3);
      expect(fake.sentFrames[2].messageType, MessageType.inputSnapshot);
      expect(fake.sentFrames[2].sequence, 2);
    });

    test('AUTH_OK with mismatched sessionId answers ERROR invalid-message',
        () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      session.connect();
      await session.waitForIdle();
      fake.emit(_frame(MessageType.welcome, WelcomePayloadCodec.encode(welcome())));
      await session.waitForIdle();
      fake.emit(_frame(
          MessageType.authOk,
          AuthOkPayloadCodec.encode(
              authOk(Uint8List.fromList(List.generate(16, (i) => 0xFF))))));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.disconnected);
      expect(fake.sentFrames.last.messageType, MessageType.error);
      expect(ErrorPayloadCodec.decode(fake.sentFrames.last.payload).code,
          errorCodeInvalidMessage);
    });

    test('WELCOME with unsupported effective version answers ERROR version',
        () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      session.connect();
      await session.waitForIdle();
      fake.emit(_frame(
        MessageType.welcome,
        WelcomePayloadCodec.encode(WelcomePayload(
          serverName: 'CTRL-PC',
          effectiveMajor: 2,
          effectiveMinor: 0,
          minSupportedMajor: 1,
          sessionId: sessionId,
          authRequired: true,
          challenge: challenge,
        )),
      ));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.disconnected);
      expect(ErrorPayloadCodec.decode(fake.sentFrames.last.payload).code,
          errorCodeProtocolVersionMismatch);
    });

    test('connect() requires a connected transport', () {
      final fake = FakeTransport()..isConnected = false;
      final session = newSession(fake, RecordingListener());
      expect(() => session.connect(), throwsStateError);
    });

    test('reconnect() requires the reconnecting state', () {
      final fake = FakeTransport();
      final session = newSession(fake, RecordingListener());
      expect(() => session.reconnect(), throwsStateError);
    });
  });

  group('ClientSession end-of-session', () {
    test('AUTH_DENIED moves to disconnected', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      session.connect();
      await session.waitForIdle();
      fake.emit(_frame(MessageType.welcome, WelcomePayloadCodec.encode(welcome())));
      await session.waitForIdle();
      fake.emit(_frame(
          MessageType.authDenied,
          AuthDeniedPayloadCodec.encode(AuthDeniedPayload(
              reason: authDeniedReasonBadCredential,
              message: 'bad credential'))));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.disconnected);
      expect(listener.errors.join(), contains('Authentication denied'));
    });

    test('fatal ERROR notifies the listener and closes', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      await _toReady(session, fake);
      fake.emit(_frame(
          MessageType.error,
          ErrorPayloadCodec.encode(ErrorPayload(
              code: errorCodeServerShutdown,
              severity: errorSeverityFatal,
              message: 'bye'))));
      await session.waitForIdle();
      expect(listener.fatalErrors.length, 1);
      expect(listener.fatalErrors[0].code, errorCodeServerShutdown);
      expect(session.state, ClientSessionState.disconnected);
    });

    test('non-fatal ERROR is logged but does not close', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      await _toReady(session, fake);
      fake.emit(_frame(
          MessageType.error,
          ErrorPayloadCodec.encode(ErrorPayload(
              code: errorCodeDeviceLimit,
              severity: errorSeverityWarn,
              message: 'limit'))));
      await session.waitForIdle();
      expect(listener.fatalErrors, isEmpty);
      expect(session.state, ClientSessionState.ready);
    });

    test('DISCONNECT closes deliberately', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      await _toReady(session, fake);
      fake.emit(_frame(
          MessageType.disconnect,
          DisconnectPayloadCodec.encode(
              DisconnectPayload(reason: disconnectReasonNormal))));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.disconnected);
    });
  });

  group('ClientSession input-plane', () {
    test('INPUT_RESET re-sends the snapshot', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      await _toReady(session, fake);
      final countBefore = fake.sentFrames.length;
      fake.emit(_frame(
          MessageType.inputReset,
          InputResetPayloadCodec.encode(
              InputResetPayload(reason: inputResetReasonStateReset))));
      await session.waitForIdle();
      expect(fake.sentFrames.length, countBefore + 1);
      expect(fake.sentFrames.last.messageType, MessageType.inputSnapshot);
    });

    test('INPUT_RESET out of state answers ERROR invalid-message', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      session.connect();
      await session.waitForIdle();
      fake.emit(_frame(
          MessageType.inputReset,
          InputResetPayloadCodec.encode(
              InputResetPayload(reason: inputResetReasonStateReset))));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.disconnected);
      expect(ErrorPayloadCodec.decode(fake.sentFrames.last.payload).code,
          errorCodeInvalidMessage);
    });
  });

  group('ClientSession keepalive', () {
    test('PONG is delivered to the listener', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener, nowMs: () => 123);
      await _toReady(session, fake);
      fake.emit(_frame(MessageType.pong, PongPayloadCodec.encode(
          PongPayload(clientSendTime: 42, serverTime: 987))));
      await session.waitForIdle();
      expect(listener.pongs.length, 1);
      expect(listener.pongs[0].clientSendTime, 42);
      expect(listener.pongs[0].serverTime, 987);
    });

    test('ACK_REQUESTED inbound is answered with ACK', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener, nowMs: () => 555);
      await _toReady(session, fake);
      fake.emit(_frame(
        MessageType.inputReset,
        InputResetPayloadCodec.encode(
            InputResetPayload(reason: inputResetReasonStateReset)),
        sequence: 9,
        ackRequested: true,
      ));
      await session.waitForIdle();
      final ack = fake.sentFrames[fake.sentFrames.length - 2];
      expect(ack.messageType, MessageType.ack);
      final ackPayload = AckPayloadCodec.decode(ack.payload);
      expect(ackPayload.ackedSequence, 9);
      expect(ackPayload.ackTime, 555);
      expect(fake.sentFrames.last.messageType, MessageType.inputSnapshot);
    });
  });

  group('ClientSession error paths', () {
    test('reserved flags answer ERROR forbidden and close (D2)', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      session.connect();
      await session.waitForIdle();
      fake.emit(_frame(
          MessageType.hello,
          HelloPayloadCodec.encode(HelloPayload(
              deviceId: deviceId,
              clientVersion: '0.1.0',
              protocolMajor: 1,
              protocolMinor: 0,
              capabilities: 7)),
          flags: FrameCodec.secure));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.disconnected);
      expect(fake.sentFrames.last.messageType, MessageType.error);
      expect(ErrorPayloadCodec.decode(fake.sentFrames.last.payload).code,
          errorCodeForbidden);
    });

    test('server HELLO is a wrong-direction invalid-message', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      session.connect();
      await session.waitForIdle();
      fake.emit(_frame(
          MessageType.hello,
          HelloPayloadCodec.encode(HelloPayload(
              deviceId: deviceId,
              clientVersion: '0.1.0',
              protocolMajor: 1,
              protocolMinor: 0,
              capabilities: 7))));
      await session.waitForIdle();
      expect(ErrorPayloadCodec.decode(fake.sentFrames.last.payload).code,
          errorCodeInvalidMessage);
      expect(session.state, ClientSessionState.disconnected);
    });

    test('wajib-dipahami unknown type answers ERROR unsupported-message',
        () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      session.connect();
      await session.waitForIdle();
      fake.emit(
          _frame(MessageType.profileList, Uint8List(1), mustUnderstand: true));
      await session.waitForIdle();
      expect(fake.sentFrames.last.messageType, MessageType.error);
      expect(ErrorPayloadCodec.decode(fake.sentFrames.last.payload).code,
          errorCodeUnsupportedMessage);
    });

    test('unknown non-wajib type is ignored', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      session.connect();
      await session.waitForIdle();
      fake.emit(_frame(0x20, Uint8List(1)));
      await session.waitForIdle();
      expect(fake.sentFrames.length, 1, reason: 'only HELLO sent');
      expect(session.state, ClientSessionState.waitWelcome);
    });
  });

  group('ClientSession reconnect', () {
    test('unexpected disconnect -> reconnecting; manual reconnect re-handshakes',
        () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      await _toReady(session, fake);

      fake.emitDisconnected('peer closed');
      expect(session.state, ClientSessionState.reconnecting);

      fake.isConnected = true;
      session.reconnect();
      await session.waitForIdle();
      expect(session.state, ClientSessionState.waitWelcome);
      expect(fake.sentFrames.last.messageType, MessageType.hello);
      expect(fake.sentFrames.last.sequence, 0, reason: 'fresh session sequence');
    });

    test('reconnect() requires a reconnected transport', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      await _toReady(session, fake);
      fake.emitDisconnected('peer closed');
      expect(session.state, ClientSessionState.reconnecting);
      fake.isConnected = false;
      expect(() => session.reconnect(), throwsStateError);
    });
  });
}

Future<void> _toReady(ClientSession session, FakeTransport fake) async {
  session.connect();
  await session.waitForIdle();
  fake.emit(_frame(MessageType.welcome, WelcomePayloadCodec.encode(_welcome())));
  await session.waitForIdle();
  fake.emit(_frame(MessageType.authOk, AuthOkPayloadCodec.encode(_authOk())));
  await session.waitForIdle();
  expect(session.state, ClientSessionState.ready);
}

class _FixedSnapshotProvider implements InputSnapshotProvider {
  final InputSnapshotPayload snapshot;
  _FixedSnapshotProvider()
      : snapshot = InputSnapshotPayload(events: [
          InputEvent(
            controlId: 'a',
            kind: inputEventKindButton,
            flags: 0,
            state: inputEventStateUp,
            pressCount: 0,
          ),
        ]);

  @override
  InputSnapshotPayload currentSnapshot() => snapshot;
}

class FakeTransport implements TransportConnection {
  final _frames = StreamController<ProtocolFrame>.broadcast(sync: true);
  final _disconnected = StreamController<String>.broadcast(sync: true);
  final sent = <Uint8List>[];
  final sentFrames = <ProtocolFrame>[];
  @override
  bool isConnected = true;

  @override
  Stream<ProtocolFrame> get frames => _frames.stream;

  @override
  Stream<String> get disconnected => _disconnected.stream;

  void emit(ProtocolFrame frame) => _frames.add(frame);

  void emitDisconnected(String reason) => _disconnected.add(reason);

  @override
  Future<void> send(Uint8List frame) async {
    final copy = Uint8List.fromList(frame);
    sent.add(copy);
    sentFrames.add(FrameCodec.decode(copy));
  }

  @override
  Future<void> close() async {
    isConnected = false;
    await _frames.close();
    await _disconnected.close();
  }
}

class RecordingListener implements SessionListener {
  final states = <ClientSessionState>[];
  final errors = <String>[];
  final fatalErrors = <ErrorPayload>[];
  final pongs = <PongPayload>[];

  @override
  void onStateChanged(ClientSessionState state) => states.add(state);

  @override
  void onError(String message) => errors.add(message);

  @override
  void onFatalError(ErrorPayload error) => fatalErrors.add(error);

  @override
  void onPong(PongPayload pong) => pongs.add(pong);
}

ProtocolFrame _frame(
  int type,
  Uint8List payload, {
  int sequence = 0,
  bool ackRequested = false,
  bool mustUnderstand = false,
  int versionMajor = 1,
  int flags = 0,
}) {
  var f = flags;
  if (ackRequested) {
    f |= FrameCodec.ackRequested;
  }
  if (mustUnderstand) {
    f |= FrameCodec.mustUnderstand;
  }
  return ProtocolFrame(
    versionMajor: versionMajor,
    versionMinor: 0,
    flags: f,
    messageType: type,
    sequence: sequence,
    timestamp: 0,
    payload: payload,
  );
}

// Standalone fixed welcome/authOk used by _toReady (sessionId/challenge reused
// across the file scope variables is not possible in a top-level helper, so
// these construct fixed values again).
Uint8List _sessionIdBytes() => Uint8List.fromList(List.generate(16, (i) => i));

Uint8List _challengeBytes() =>
    Uint8List.fromList(List.generate(32, (i) => 0x10 + i));

WelcomePayload _welcome() => WelcomePayload(
      serverName: 'CTRL-PC',
      effectiveMajor: 1,
      effectiveMinor: 0,
      minSupportedMajor: 1,
      sessionId: _sessionIdBytes(),
      authRequired: true,
      challenge: _challengeBytes(),
    );

AuthOkPayload _authOk() => AuthOkPayload(
      result: authOkResultOk,
      sessionId: _sessionIdBytes(),
      serverCapabilities: 0x00000007,
      newToken: Uint8List(0),
    );