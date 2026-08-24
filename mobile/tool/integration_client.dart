import 'dart:io';
import 'dart:typed_data';

import 'package:ctrl_mobile/protocol/auth_ok_payload.dart';
import 'package:ctrl_mobile/protocol/error_payload.dart';
import 'package:ctrl_mobile/protocol/frame.dart';
import 'package:ctrl_mobile/protocol/frame_builder.dart';
import 'package:ctrl_mobile/protocol/input_event.dart';
import 'package:ctrl_mobile/protocol/input_snapshot_payload.dart';
import 'package:ctrl_mobile/protocol/message_types.dart';
import 'package:ctrl_mobile/protocol/pong_payload.dart';
import 'package:ctrl_mobile/protocol/welcome_payload.dart';
import 'package:ctrl_mobile/session/authenticator.dart';
import 'package:ctrl_mobile/session/client_session.dart';
import 'package:ctrl_mobile/session/input_snapshot_provider.dart';
import 'package:ctrl_mobile/session/session_listener.dart';
import 'package:ctrl_mobile/session/session_state.dart';
import 'package:ctrl_mobile/session/token_store.dart';
import 'package:ctrl_mobile/transport/tcp_transport.dart';
import 'package:ctrl_mobile/transport/transport_connection.dart';

/// M1.4.3 cross-language integration client.
///
/// Connects to the C# integration server (`dotnet run --project desktop --
/// --integration 0`) over REAL TCP and proves both frame directions:
///   - Dart FrameBuilder -> C# FrameCodec (server markers printed by the host)
///   - C# FrameBuilder -> Dart FrameCodec (assertions below on decoded frames)
///
/// Phases:
///   1. happy path: HELLO/WELCOME/AUTH/AUTH_OK -> auto INPUT_SNAPSHOT ->
///      INPUT_EVENT x2 -> HEARTBEAT -> PONG echo -> graceful DISCONNECT
///   2. reconnect: fresh TCP + session, full handshake + snapshot again
///   3. takeover: third session with the SAME deviceId displaces phase-2
///      session (client observes ERROR device-limit [severity warn per §24]
///      followed by an unexpected close -> reconnecting state)
///   4. invalid message: unknown type + MUST_UNDERSTAND answered with ERROR
///      unsupported-message and a server-side close
///
/// Prints DART:* progress markers; prints DART:INTEGRATION:PASS and exits 0
/// only if every assertion holds.
Future<void> main(List<String> args) async {
  var port = -1;
  var host = InternetAddress.loopbackIPv4.address;
  for (var i = 0; i < args.length; i++) {
    if (args[i] == '--port' && i + 1 < args.length) {
      port = int.parse(args[i + 1]);
    }
    if (args[i] == '--host' && i + 1 < args.length) {
      host = args[i + 1];
    }
  }
  if (port <= 0) {
    stderr.writeln('usage: integration_client.dart --port <port>');
    exit(2);
  }

  try {
    await _run(host, port);
    stdout.writeln('DART:INTEGRATION:PASS');
    exit(0);
  } on _IntegrationFailure catch (e) {
    stdout.writeln('DART:FAIL:${e.message}');
    exit(1);
  } catch (e) {
    stdout.writeln('DART:FAIL:unexpected: $e');
    exit(1);
  }
}

Future<void> _run(String host, int port) async {
  const deviceId = 'integration-device';
  // Pinned pairing code — the C# integration server issues exactly this one
  // single-use code at startup (AuthTestEnv.PairingCode).
  const pairingCode = '123456';
  final tokenStore = InMemoryTokenStore();

  // ---- Phase 1: happy path (pairing) ---------------------------------------
  final tapA = _TapListener();
  final transportA = TcpTransport(port: port);
  await transportA.connect();
  final wireA = <ProtocolFrame>[];
  transportA.frames.listen(wireA.add);
  final recordingA = _RecordingTransport(transportA);
  var clockA = 1000;
  final sessionA = ClientSession(
    transport: recordingA,
    authenticator:
        HmacAuthenticator.pairing(pairingCode: pairingCode, deviceId: deviceId),
    listener: tapA,
    inputSnapshotProvider: _IntegrationSnapshotProvider(),
    tokenStore: tokenStore,
    nowMs: () => clockA,
  );
  sessionA.connect();
  await _waitFor(() => sessionA.state == ClientSessionState.ready,
      label: 'session A reaches ready');
  _marker('phase1 ready');

  // C# -> Dart: WELCOME decodes with the expected contract fields.
  final welcomeFrame = wireA.singleWhere(
      (f) => f.messageType == MessageType.welcome);
  final welcome = WelcomePayloadCodec.decode(welcomeFrame.payload);
  _expect(welcome.serverName == 'CTRL-PC', 'WELCOME serverName is CTRL-PC');
  _expect(welcome.effectiveMajor == 1 && welcome.effectiveMinor == 0,
      'WELCOME effective version is 1.0');
  _expect(welcome.sessionId.length == 16, 'WELCOME sessionId is 16 bytes');
  _expect(welcome.challenge.length == 32, 'WELCOME challenge is 32 bytes');
  _expect(welcome.authRequired, 'WELCOME authRequired is true');

  // C# -> Dart: AUTH_OK echoes the WELCOME sessionId and issues newToken.
  final authOkFrame =
      wireA.singleWhere((f) => f.messageType == MessageType.authOk);
  final authOk = AuthOkPayloadCodec.decode(authOkFrame.payload);
  _expect(_bytesEqual(authOk.sessionId, welcome.sessionId),
      'AUTH_OK echoes the WELCOME sessionId');
  _expect(authOk.result == authOkResultOk, 'AUTH_OK result is ok');
  _expect(authOk.serverCapabilities != 0, 'AUTH_OK carries capabilities');
  _expect(authOk.newToken.length == 32,
      'pairing AUTH_OK issues a 32-byte newToken');
  final savedToken = tokenStore.load(deviceId);
  _expect(savedToken != null && _bytesEqual(savedToken, authOk.newToken),
      'client stored the issued newToken (§12)');
  final tokenForReconnect = Uint8List.fromList(savedToken!);

  // Dart -> C#: HELLO(0), AUTH(1), then the mandatory snapshot restarts at 0.
  _expect(recordingA.sentFrames.length == 3, 'three frames sent by phase 1');
  _expect(recordingA.sentFrames[0].messageType == MessageType.hello &&
          recordingA.sentFrames[0].sequence == 0,
      'HELLO is the first frame with sequence 0');
  _expect(recordingA.sentFrames[1].messageType == MessageType.auth &&
          recordingA.sentFrames[1].sequence == 1,
      'AUTH follows with sequence 1');
  final snapshotFrame = recordingA.sentFrames[2];
  _expect(snapshotFrame.messageType == MessageType.inputSnapshot,
      'INPUT_SNAPSHOT is sent immediately after AUTH_OK');
  _expect(snapshotFrame.sequence == 0,
      'snapshot sequence restarts at 0 after AUTH_OK');
  final snapshot = InputSnapshotPayloadCodec.decode(snapshotFrame.payload);
  _expect(snapshot.events.length == 2, 'snapshot carries both controls');
  _expect(
      snapshot.events
          .every((e) => (e.flags & inputEventFlagInitial) != 0),
      'every snapshot entry sets the initial flag');

  // Dart -> C# hot path: one button event and one axis event.
  sessionA.sendInputEvent(const InputEvent(
    controlId: 'btn-fire',
    kind: inputEventKindButton,
    flags: inputEventFlagStateChanged,
    state: inputEventStateDown,
    pressCount: 1,
  ));
  sessionA.sendInputEvent(const InputEvent(
    controlId: 'thr',
    kind: inputEventKindTrigger,
    flags: inputEventFlagStateChanged,
    value: 0.5,
  ));
  await sessionA.waitForIdle();
  _expect(recordingA.sentFrames.length == 5, 'two INPUT_EVENTs appended');
  _expect(recordingA.sentFrames[3].sequence == 1 &&
      recordingA.sentFrames[4].sequence == 2,
      'post-auth sequences increment 1, 2');
  _marker('phase1 events sent');

  // Heartbeat -> PONG echo (§10): PONG is not an ACK.
  clockA = 12345;
  sessionA.sendHeartbeat();
  await sessionA.waitForIdle();
  await _waitFor(() => tapA.pongs.isNotEmpty, label: 'PONG arrives');
  _expect(tapA.pongs.first.clientSendTime == 12345,
      'PONG echoes clientSendTime');
  _expect(tapA.pongs.first.serverTime > 0, 'PONG carries serverTime');
  _expect(
      wireA.every((f) => f.messageType != MessageType.ack),
      'no ACK is used on the HEARTBEAT path');
  _marker('phase1 pong echoed');

  // Graceful disconnect (§14): DISCONNECT then close.
  await sessionA.sendDisconnect();
  await _waitFor(() => !transportA.isConnected,
      label: 'transport A closed');
  _marker('phase1 graceful disconnect done');

  // ---- Phase 2: reconnect with the issued token ----------------------------
  final tapB = _TapListener();
  final transportB = TcpTransport(port: port);
  await transportB.connect();
  final wireB = <ProtocolFrame>[];
  transportB.frames.listen(wireB.add);
  final recordingB = _RecordingTransport(transportB);
  final sessionB = ClientSession(
    transport: recordingB,
    authenticator:
        HmacAuthenticator.token(token: tokenForReconnect, deviceId: deviceId),
    listener: tapB,
    inputSnapshotProvider: _IntegrationSnapshotProvider(),
    tokenStore: tokenStore,
  );
  sessionB.connect();
  await _waitFor(() => sessionB.state == ClientSessionState.ready,
      label: 'reconnected session B reaches ready');
  _expect(recordingB.sentFrames[2].messageType == MessageType.inputSnapshot &&
          recordingB.sentFrames[2].sequence == 0,
      'reconnect resyncs with a fresh INPUT_SNAPSHOT at sequence 0');
  // §12 A: a successful TOKEN auth never issues another newToken.
  final authOkB = AuthOkPayloadCodec.decode(wireB
      .singleWhere((f) => f.messageType == MessageType.authOk)
      .payload);
  _expect(authOkB.newToken.isEmpty,
      'token-reconnect AUTH_OK must not carry newToken');
  _marker('phase2 reconnect ready');

  // ---- Phase 3: takeover ---------------------------------------------------
  final tapC = _TapListener();
  final transportC = TcpTransport(port: port);
  await transportC.connect();
  final recordingC = _RecordingTransport(transportC);
  final wireC = <ProtocolFrame>[];
  transportC.frames.listen(wireC.add);
  final sessionC = ClientSession(
    transport: recordingC,
    authenticator:
        HmacAuthenticator.token(token: tokenForReconnect, deviceId: deviceId),
    listener: tapC,
    inputSnapshotProvider: _IntegrationSnapshotProvider(),
    tokenStore: tokenStore,
  );
  sessionC.connect();
  await _waitFor(() => sessionC.state == ClientSessionState.ready,
      label: 'takeover session C reaches ready');
  // protocol.md §24: device-limit ERROR carries severity warn, so it surfaces
  // via onError; the server-side close then lands the old session in
  // reconnecting (unexpected close from the client's perspective, D8).
  await _waitFor(
      () => tapB.errors.any((m) => m.contains('Peer sent ERROR 0x4')),
      label: 'displaced session B observes the takeover');
  await _waitFor(() => !transportB.isConnected,
      label: 'displaced session B socket closed');
  _expect(sessionB.state == ClientSessionState.reconnecting,
      'session B ends up reconnecting after takeover');
  _marker('phase3 takeover complete');

  // Session C keeps working as the active session.
  sessionC.sendInputEvent(const InputEvent(
    controlId: 'btn-fire',
    kind: inputEventKindButton,
    flags: inputEventFlagStateChanged,
    state: inputEventStateDown,
    pressCount: 1,
  ));
  await sessionC.waitForIdle();

  // ---- Phase 4: invalid message -------------------------------------------
  await transportC.send(FrameBuilder.build(
    messageType: 0x7A,
    payload: Uint8List.fromList([0x00]),
    sequence: 9,
    mustUnderstand: true,
    versionMajor: 1,
    versionMinor: 0,
    timestamp: 0,
  ));
  await _waitFor(() => tapC.fatalErrors.isNotEmpty,
      label: 'unknown MUST_UNDERSTAND type answered');
  _expect(tapC.fatalErrors.first.code == errorCodeUnsupportedMessage,
      'unknown wajib-dipahami type yields ERROR unsupported-message');
  await _waitFor(() => !transportC.isConnected,
      label: 'server closed session C after the violation');
  _marker('phase4 invalid message handled');

  // Cross-cutting invariant: the server never ACKed anything in this flow and
  // never sent a message type reserved for the client->server direction.
  for (final wire in [wireA]) {
    for (final f in wire) {
      _expect(
          f.messageType != MessageType.ack &&
              f.messageType != MessageType.hello &&
              f.messageType != MessageType.auth &&
              f.messageType != MessageType.inputEvent &&
              f.messageType != MessageType.inputSnapshot &&
              f.messageType != MessageType.heartbeat,
          'server only sends server->client types');
    }
  }
}

void _expect(bool condition, String what) {
  if (!condition) {
    throw _IntegrationFailure(what);
  }
}

void _marker(String step) {
  stdout.writeln('DART:OK:$step');
}

Future<void> _waitFor(bool Function() condition,
    {required String label, Duration timeout = const Duration(seconds: 10)}) async {
  final stopwatch = Stopwatch()..start();
  while (!condition()) {
    if (stopwatch.elapsed > timeout) {
      throw _IntegrationFailure('$label within ${timeout.inMilliseconds}ms');
    }
    await Future<void>.delayed(const Duration(milliseconds: 5));
  }
}

bool _bytesEqual(List<int> a, List<int> b) {
  if (a.length != b.length) {
    return false;
  }
  for (var i = 0; i < a.length; i++) {
    if (a[i] != b[i]) {
      return false;
    }
  }
  return true;
}

class _IntegrationFailure implements Exception {
  _IntegrationFailure(this.message);

  final String message;

  @override
  String toString() => message;
}

class _IntegrationSnapshotProvider implements InputSnapshotProvider {
  const _IntegrationSnapshotProvider();

  @override
  InputSnapshotPayload currentSnapshot() => InputSnapshotPayload(events: [
        const InputEvent(
          controlId: 'btn-fire',
          kind: inputEventKindButton,
          flags: inputEventFlagStateChanged | inputEventFlagInitial,
          state: inputEventStateDown,
          pressCount: 1,
        ),
        const InputEvent(
          controlId: 'thr',
          kind: inputEventKindTrigger,
          flags: inputEventFlagInitial,
          value: 0.5,
        ),
      ]);
}

class _TapListener implements SessionListener {
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

/// Records every outbound frame while delegating to the real TCP transport so
/// tests can assert exactly what was put on the wire.
class _RecordingTransport implements TransportConnection {
  _RecordingTransport(this._inner);

  final TransportConnection _inner;
  final sentFrames = <ProtocolFrame>[];

  @override
  bool get isConnected => _inner.isConnected;

  @override
  Stream<ProtocolFrame> get frames => _inner.frames;

  @override
  Stream<String> get disconnected => _inner.disconnected;

  @override
  Future<void> send(Uint8List frame) async {
    sentFrames.add(FrameCodec.decode(frame));
    await _inner.send(frame);
  }

  @override
  Future<void> close() => _inner.close();
}
