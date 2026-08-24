import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:ctrl_mobile/protocol/ack_payload.dart';
import 'package:ctrl_mobile/protocol/auth_denied_payload.dart';
import 'package:ctrl_mobile/protocol/auth_ok_payload.dart';
import 'package:ctrl_mobile/protocol/auth_payload.dart';
import 'package:ctrl_mobile/protocol/disconnect_payload.dart';
import 'package:ctrl_mobile/protocol/error_payload.dart';
import 'package:ctrl_mobile/protocol/frame.dart';
import 'package:ctrl_mobile/protocol/hello_payload.dart';
import 'package:ctrl_mobile/protocol/heartbeat_payload.dart';
import 'package:ctrl_mobile/protocol/input_event.dart';
import 'package:ctrl_mobile/protocol/input_event_payload.dart';
import 'package:ctrl_mobile/protocol/input_reset_payload.dart';
import 'package:ctrl_mobile/protocol/input_snapshot_payload.dart';
import 'package:ctrl_mobile/protocol/message_types.dart';
import 'package:ctrl_mobile/protocol/pong_payload.dart';
import 'package:ctrl_mobile/protocol/welcome_payload.dart';
import 'package:ctrl_mobile/crypto/sha256.dart';
import 'package:ctrl_mobile/session/ack_tracker.dart';
import 'package:ctrl_mobile/session/authenticator.dart';
import 'package:ctrl_mobile/session/client_session.dart';
import 'package:ctrl_mobile/session/input_snapshot_provider.dart';
import 'package:ctrl_mobile/session/sequence_tracker.dart';
import 'package:ctrl_mobile/session/session_listener.dart';
import 'package:ctrl_mobile/session/session_state.dart';
import 'package:ctrl_mobile/session/token_store.dart';
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
      authenticator: _testTokenAuthenticator(deviceId),
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

    test('outbound wraps 65535 -> 0 (M1.4.3)', () {
      final outbound = SequenceTracker(start: 65534);
      expect(outbound.next(), 65534);
      expect(outbound.next(), 65535);
      expect(outbound.next(), 0);
      expect(outbound.next(), 1);

      final inbound = InboundSequenceTracker();
      expect(inbound.isMonotonic(65534), isTrue);
      expect(inbound.isMonotonic(65535), isTrue);
      expect(inbound.isMonotonic(0), isTrue, reason: 'wrap 65535 -> 0 accepted');
      expect(inbound.isMonotonic(1), isTrue);
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

    test('retransmit reschedules the deadline without resetting attempts (M1.4.3)',
        () {
      var now = 0;
      final tracker = AckTracker(nowMs: () => now, timeoutMs: 3000);
      tracker.track(10);
      now = 3000;
      expect(tracker.retryExpired(), [10], reason: 'first timeout');
      now = 4000;
      tracker.reschedule(10); // retransmit at t=4000
      now = 6999;
      expect(tracker.failed(), isEmpty, reason: 'no fail before retry deadline');
      now = 7000;
      expect(tracker.failed(), [10], reason: 'fail after retry deadline');
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
    test('HELLO(seq0) -> WELCOME -> AUTH(seq1) -> AUTH_OK -> snapshot(seq0 reset)',
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
      expect(auth.challengeResponse.length, 32,
          reason: '§12: challengeResponse stays exactly 32 bytes');
      expect(
          auth.challengeResponse, isNot(orderedEquals(challenge)),
          reason: 'M1.4.4: response is HMAC-SHA256(secret, challenge), '
              'not a challenge echo');

      fake.emit(_frame(MessageType.authOk, AuthOkPayloadCodec.encode(authOk())));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.ready);
      expect(fake.sentFrames.length, 3);
      expect(fake.sentFrames[2].messageType, MessageType.inputSnapshot);
      expect(fake.sentFrames[2].sequence, 0,
          reason: '§7/§24.5: sequence restarts at 0 after AUTH_OK');
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

  group('ClientSession state transitions', () {
    test('connect passes through connecting -> connected -> waitWelcome', () {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      session.connect();
      expect(listener.states, [
        ClientSessionState.connecting,
        ClientSessionState.connected,
        ClientSessionState.waitWelcome,
      ]);
      expect(session.state, ClientSessionState.waitWelcome);
    });

    test('WELCOME advances waitWelcome -> waitAuth -> waitAuthOk', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      session.connect();
      await session.waitForIdle();
      fake.emit(_frame(MessageType.welcome, WelcomePayloadCodec.encode(welcome())));
      await session.waitForIdle();
      expect(listener.states, [
        ClientSessionState.connecting,
        ClientSessionState.connected,
        ClientSessionState.waitWelcome,
        ClientSessionState.waitAuth,
        ClientSessionState.waitAuthOk,
      ]);
      expect(session.state, ClientSessionState.waitAuthOk);
      expect(fake.sentFrames.last.messageType, MessageType.auth);
    });

    test('reconnect passes through connecting -> connected -> waitWelcome', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      await _toReady(session, fake);
      listener.states.clear();
      fake.emitDisconnected('peer closed');
      expect(session.state, ClientSessionState.reconnecting);
      fake.isConnected = true;
      session.reconnect();
      expect(listener.states, [
        ClientSessionState.reconnecting,
        ClientSessionState.connecting,
        ClientSessionState.connected,
        ClientSessionState.waitWelcome,
      ]);
    });
  });

  group('ClientSession outbound (M1.4.3)', () {
    test('sendInputEvent is guarded before ready', () async {
      final fake = FakeTransport();
      final session = newSession(fake, RecordingListener());
      session.connect();
      await session.waitForIdle();
      expect(
        () => session.sendInputEvent(_buttonEvent()),
        throwsStateError,
      );
    });

    test('sendInputEvent sends a single INPUT_EVENT after ready', () async {
      final fake = FakeTransport();
      final session = newSession(fake, RecordingListener());
      await _toReady(session, fake);
      session.sendInputEvent(_buttonEvent());
      await session.waitForIdle();
      expect(fake.sentFrames.last.messageType, MessageType.inputEvent);
      expect(fake.sentFrames.last.sequence, 1,
          reason: 'snapshot reset the counter to 0; event follows at 1');
      final decoded =
          InputEventPayloadCodec.decode(fake.sentFrames.last.payload);
      expect(decoded.event.controlId, 'btn-fire');
      expect(decoded.event.state, inputEventStateDown);
      expect(decoded.event.pressCount, 1);
    });

    test('sendHeartbeat is guarded before ready', () async {
      final fake = FakeTransport();
      final session = newSession(fake, RecordingListener());
      session.connect();
      await session.waitForIdle();
      expect(() => session.sendHeartbeat(), throwsStateError);
    });

    test('sendHeartbeat sends HEARTBEAT carrying clientSendTime', () async {
      final fake = FakeTransport();
      final session = newSession(fake, RecordingListener(), nowMs: () => 4242);
      await _toReady(session, fake);
      session.sendHeartbeat();
      await session.waitForIdle();
      expect(fake.sentFrames.last.messageType, MessageType.heartbeat);
      expect(
        HeartbeatPayloadCodec.decode(fake.sentFrames.last.payload).clientSendTime,
        4242,
      );
    });

    test('sendWithAck retries once (same sequence) then closes on final timeout',
        () async {
      var now = 0;
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener, nowMs: () => now);
      await _toReady(session, fake);
      session.sendWithAck(
        type: MessageType.inputReset,
        payload: InputResetPayloadCodec.encode(
            InputResetPayload(reason: inputResetReasonStateReset)),
      );
      await session.waitForIdle();
      final seq = fake.sentFrames.last.sequence;
      expect(fake.sentFrames.last.flags & FrameCodec.ackRequested, FrameCodec.ackRequested);

      now = 3000;
      session.processPendingAcks();
      await session.waitForIdle();
      expect(listener.errors.join(), contains('Retransmitting'));
      expect(fake.sentFrames.last.sequence, seq, reason: 'retry reuses sequence');

      now = 6000;
      session.processPendingAcks();
      await session.waitForIdle();
      expect(session.state, ClientSessionState.disconnected,
          reason: 'final timeout closes the session');
    });

    test('ACK for an unknown/wrong sequence is a harmless no-op', () async {
      final fake = FakeTransport();
      final session = newSession(fake, RecordingListener(), nowMs: () => 0);
      await _toReady(session, fake);
      session.sendWithAck(
        type: MessageType.inputReset,
        payload: InputResetPayloadCodec.encode(
            InputResetPayload(reason: inputResetReasonStateReset)),
      );
      await session.waitForIdle();
      final seq = fake.sentFrames.last.sequence;
      fake.emit(_frame(
        MessageType.ack,
        AckPayloadCodec.encode(AckPayload(ackedSequence: seq + 1, ackTime: 0)),
      ));
      fake.emit(_frame(
        MessageType.ack,
        AckPayloadCodec.encode(AckPayload(ackedSequence: seq + 1, ackTime: 0)),
      ));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.ready);
    });

    test('sendDisconnect sends DISCONNECT then closes', () async {
      final fake = FakeTransport();
      final session = newSession(fake, RecordingListener());
      await _toReady(session, fake);
      await session.sendDisconnect(reason: disconnectReasonNormal);
      expect(fake.sentFrames.last.messageType, MessageType.disconnect);
      expect(
        DisconnectPayloadCodec.decode(fake.sentFrames.last.payload).reason,
        disconnectReasonNormal,
      );
      expect(session.state, ClientSessionState.disconnected);
    });

    test('normal ACK clears pending without retry (M1.4.3)', () async {
      var now = 0;
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener, nowMs: () => now);
      await _toReady(session, fake);
      session.sendWithAck(
        type: MessageType.inputReset,
        payload: InputResetPayloadCodec.encode(
            InputResetPayload(reason: inputResetReasonStateReset)),
      );
      await session.waitForIdle();
      final seq = fake.sentFrames.last.sequence;
      final framesBefore = fake.sentFrames.length;
      fake.emit(_frame(
        MessageType.ack,
        AckPayloadCodec.encode(AckPayload(ackedSequence: seq, ackTime: 1)),
        sequence: 5,
      ));
      await session.waitForIdle();
      now = 3000;
      session.processPendingAcks();
      await session.waitForIdle();
      expect(session.state, ClientSessionState.ready,
          reason: 'ACKed message must not fail the session');
      expect(listener.errors.join(), isNot(contains('Retransmitting')));
      expect(fake.sentFrames.length, framesBefore,
          reason: 'no retransmit may follow a normal ACK');
    });

    test('duplicate ACK for the same sequence is harmless (M1.4.3)', () async {
      var now = 0;
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener, nowMs: () => now);
      await _toReady(session, fake);
      session.sendWithAck(
        type: MessageType.inputReset,
        payload: InputResetPayloadCodec.encode(
            InputResetPayload(reason: inputResetReasonStateReset)),
      );
      await session.waitForIdle();
      final seq = fake.sentFrames.last.sequence;
      fake.emit(_frame(
        MessageType.ack,
        AckPayloadCodec.encode(AckPayload(ackedSequence: seq, ackTime: 1)),
        sequence: 5,
      ));
      fake.emit(_frame(
        MessageType.ack,
        AckPayloadCodec.encode(AckPayload(ackedSequence: seq, ackTime: 2)),
        sequence: 6,
      ));
      await session.waitForIdle();
      now = 6000;
      session.processPendingAcks();
      await session.waitForIdle();
      expect(session.state, ClientSessionState.ready,
          reason: 'duplicate ACKs must not corrupt tracker state');
    });

    test('late ACK after the final timeout cannot resurrect the session',
        () async {
      var now = 0;
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener, nowMs: () => now);
      await _toReady(session, fake);
      session.sendWithAck(
        type: MessageType.inputReset,
        payload: InputResetPayloadCodec.encode(
            InputResetPayload(reason: inputResetReasonStateReset)),
      );
      await session.waitForIdle();
      final seq = fake.sentFrames.last.sequence;
      final framesBefore = fake.sentFrames.length;

      now = 3000;
      session.processPendingAcks(); // retry once
      await session.waitForIdle();
      now = 6000;
      session.processPendingAcks(); // final timeout
      await session.waitForIdle();
      expect(session.state, ClientSessionState.disconnected);

      fake.emit(_frame(
        MessageType.ack,
        AckPayloadCodec.encode(AckPayload(ackedSequence: seq, ackTime: 9)),
        sequence: 7,
      ));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.disconnected,
          reason: 'a late ACK must not reopen a closed session');
      expect(fake.sentFrames.length, framesBefore + 1,
          reason: 'only the retransmit was added; the late ACK sends nothing');
    });
  });

  group('ClientSession sequence boundary (M1.4.3)', () {
    test('outbound counter restarts at 0 after AUTH_OK and increments',
        () async {
      final fake = FakeTransport();
      final session = newSession(fake, RecordingListener());
      await _toReady(session, fake);
      expect(fake.sentFrames[2].sequence, 0,
          reason: '§7/§24.5: INPUT_SNAPSHOT is the first post-AUTH_OK message');
      session.sendHeartbeat();
      await session.waitForIdle();
      expect(fake.sentFrames.last.sequence, 1);
      session.sendInputEvent(_buttonEvent());
      await session.waitForIdle();
      expect(fake.sentFrames.last.sequence, 2);
    });

    test('inbound tracker resets at AUTH_OK so PONG seq 0 raises no anomaly',
        () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      session.connect();
      await session.waitForIdle();
      fake.emit(_frame(
          MessageType.welcome, WelcomePayloadCodec.encode(welcome()),
          sequence: 0));
      await session.waitForIdle();
      fake.emit(
          _frame(MessageType.authOk, AuthOkPayloadCodec.encode(authOk()),
              sequence: 1));
      await session.waitForIdle();
      // The server->client direction restarts at 0 after AUTH_OK.
      fake.emit(_frame(
          MessageType.pong,
          PongPayloadCodec.encode(
              PongPayload(clientSendTime: 7, serverTime: 8)),
          sequence: 0));
      await session.waitForIdle();
      expect(listener.pongs.length, 1);
      expect(listener.pongs.first.clientSendTime, 7,
          reason: 'PONG must echo clientSendTime (§10)');
      expect(
        listener.errors.where((e) => e.contains('Non-monotonic')),
        isEmpty,
        reason: 'inbound tracker must be reset at the AUTH_OK boundary',
      );
    });

    test('reconnect restarts the post-AUTH_OK counter from 0', () async {
      final fake = FakeTransport();
      final session = newSession(fake, RecordingListener());
      await _toReady(session, fake);
      expect(fake.sentFrames[2].sequence, 0);

      fake.emitDisconnected('peer closed');
      fake.isConnected = true;
      session.reconnect();
      await session.waitForIdle();
      fake.emit(
          _frame(MessageType.welcome, WelcomePayloadCodec.encode(welcome())));
      await session.waitForIdle();
      fake.emit(
          _frame(MessageType.authOk, AuthOkPayloadCodec.encode(authOk())));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.ready);
      expect(fake.sentFrames.last.messageType, MessageType.inputSnapshot);
      expect(fake.sentFrames.last.sequence, 0,
          reason: 'a fresh post-AUTH_OK counter after reconnect');
    });
  });

  group('ClientSession snapshot boundary (M1.4.3)', () {
    test('first app-plane message after AUTH_OK is the snapshot with initial flag',
        () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = ClientSession(
        transport: fake,
        authenticator: _testTokenAuthenticator(deviceId),
        listener: listener,
        inputSnapshotProvider: _InitialFlagSnapshotProvider(),
      );
      await _toReady(session, fake);
      final snapshot = fake.sentFrames[2];
      expect(snapshot.messageType, MessageType.inputSnapshot);
      final payload =
          InputSnapshotPayloadCodec.decode(snapshot.payload);
      expect(payload.events[0].flags & inputEventFlagInitial, inputEventFlagInitial,
          reason: 'snapshot entries must carry the initial flag (§15)');
    });

    test('reconnect re-sends the snapshot after AUTH_OK', () async {
      final fake = FakeTransport();
      final listener = RecordingListener();
      final session = newSession(fake, listener);
      await _toReady(session, fake);
      expect(fake.sentFrames[2].messageType, MessageType.inputSnapshot);

      fake.emitDisconnected('peer closed');
      fake.isConnected = true;
      session.reconnect();
      await session.waitForIdle();
      fake.emit(_frame(MessageType.welcome, WelcomePayloadCodec.encode(welcome())));
      await session.waitForIdle();
      fake.emit(_frame(MessageType.authOk, AuthOkPayloadCodec.encode(authOk())));
      await session.waitForIdle();
      expect(session.state, ClientSessionState.ready);
      expect(fake.sentFrames.last.messageType, MessageType.inputSnapshot,
          reason: 'reconnect must resync via snapshot (§13/§15)');
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

  group('M1.4.4: HmacAuthenticator (docs/protocol.md §12)', () {
    test('challengeResponse is exactly 32 bytes and matches the pinned '
        'cross-language vector', () {
      final auth = HmacAuthenticator.pairing(
          pairingCode: 'ctrl-m144-cross-vector-secret', deviceId: 'd1');
      final challenge = Uint8List.fromList(List.generate(32, (i) => i));
      final mac = auth.challengeResponseFor(challenge);
      expect(mac.length, 32);
      expect(bytesToHex(mac),
          'ce7542e18060a6367f4b393b7203b929bc5b2875d0a17f0be67e71a49210a23f');
    });

    test('token secret vector matches the C# HMACSHA256 reference', () {
      final auth = HmacAuthenticator.token(
        token: Uint8List.fromList(utf8.encode('ctrl-m144-token-secret')),
        deviceId: 'd1',
      );
      final challenge = Uint8List.fromList(List.generate(32, (i) => i));
      expect(bytesToHex(auth.challengeResponseFor(challenge)),
          '6a074159523a97c4e184ee965814fe428ece623530925f54a5ea6194bdd18945');
    });

    test('different secrets yield different responses; modified challenge too',
        () {
      final a = HmacAuthenticator.pairing(
          pairingCode: '111111', deviceId: 'd1');
      final b = HmacAuthenticator.pairing(
          pairingCode: '222222', deviceId: 'd1');
      final challenge = Uint8List.fromList(List.filled(32, 7));
      expect(
        bytesToHex(a.challengeResponseFor(challenge)),
        isNot(bytesToHex(b.challengeResponseFor(challenge))),
      );
      final mutated = Uint8List.fromList(challenge);
      mutated[0] ^= 0xFF;
      expect(
        bytesToHex(a.challengeResponseFor(challenge)),
        isNot(bytesToHex(a.challengeResponseFor(mutated))),
      );
    });

    test('buildAuth keeps the wire contract: token credential stays empty', () {
      final token =
          Uint8List.fromList(List.generate(32, (i) => i * 3 & 0xFF));
      final pairingAuth = HmacAuthenticator.pairing(
              pairingCode: '123456', deviceId: 'dev-1')
          .buildAuth(Uint8List(32));
      expect(pairingAuth.credentialType, authCredentialTypePairingCode);
      expect(pairingAuth.credential, '123456');

      final tokenAuth =
          HmacAuthenticator.token(token: token, deviceId: 'dev-1')
              .buildAuth(Uint8List(32));
      expect(tokenAuth.credentialType, authCredentialTypeToken);
      expect(tokenAuth.credential, isEmpty,
          reason: '§12 A: credentialLength=0 for token auth');
    });
  });

  group('M1.4.4: newToken handling (§12)', () {
    test('AUTH_OK with newToken stores it in the TokenStore', () async {
      final fake = FakeTransport();
      final store = InMemoryTokenStore();
      final session = ClientSession(
        transport: fake,
        authenticator:
            _testTokenAuthenticator(deviceId, secret: utf8.encode('pw')),
        listener: RecordingListener(),
        inputSnapshotProvider: _FixedSnapshotProvider(),
        tokenStore: store,
      );

      session.connect();
      await session.waitForIdle();
      final issuedToken = Uint8List.fromList(
          List.generate(32, (i) => (i * 7) & 0xFF));
      final welcomeWithPairingOk = _frame(
          MessageType.welcome, WelcomePayloadCodec.encode(_welcome()));
      fake.emit(welcomeWithPairingOk);
      await session.waitForIdle();
      final authOk = AuthOkPayload(
        result: authOkResultOk,
        sessionId: _welcome().sessionId,
        serverCapabilities: 0x7,
        newToken: issuedToken,
      );
      fake.emit(_frame(MessageType.authOk, AuthOkPayloadCodec.encode(authOk)));
      await session.waitForIdle();

      expect(session.state, ClientSessionState.ready);
      final saved = store.load(deviceId);
      expect(saved, isNotNull);
      expect(saved, orderedEquals(issuedToken));
    });

    test('AUTH_OK without newToken leaves the stored token untouched',
        () async {
      final fake = FakeTransport();
      final store = InMemoryTokenStore();
      final existing = Uint8List.fromList(utf8.encode('existing-token'));
      store.save(deviceId, existing);
      final session = ClientSession(
        transport: fake,
        authenticator: _testTokenAuthenticator(deviceId),
        listener: RecordingListener(),
        inputSnapshotProvider: _FixedSnapshotProvider(),
        tokenStore: store,
      );
      await _toReady(session, fake);

      final saved = store.load(deviceId)!;
      expect(saved, orderedEquals(existing));
    });
  });
}

/// Token-flow authenticator with a fixed deterministic secret for scripted
/// server tests (the fake server never validates the HMAC itself).
HmacAuthenticator _testTokenAuthenticator(String deviceId,
    {List<int> secret = const [0x74, 0x65, 0x73, 0x74]}) {
  return HmacAuthenticator.token(
    token: Uint8List.fromList(secret),
    deviceId: deviceId,
  );
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

class _InitialFlagSnapshotProvider implements InputSnapshotProvider {
  final InputSnapshotPayload snapshot = InputSnapshotPayload(events: [
    InputEvent(
      controlId: 'a',
      kind: inputEventKindButton,
      flags: inputEventFlagStateChanged | inputEventFlagInitial,
      state: inputEventStateDown,
      pressCount: 1,
    ),
  ]);

  @override
  InputSnapshotPayload currentSnapshot() => snapshot;
}

InputEvent _buttonEvent() => InputEvent(
      controlId: 'btn-fire',
      kind: inputEventKindButton,
      flags: inputEventFlagStateChanged,
      state: inputEventStateDown,
      pressCount: 1,
    );

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

  void emit(ProtocolFrame frame) {
    if (!_frames.isClosed) {
      _frames.add(frame);
    }
  }

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