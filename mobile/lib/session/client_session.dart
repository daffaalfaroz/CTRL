import 'dart:async';
import 'dart:typed_data';

import '../protocol/ack_payload.dart';
import '../protocol/auth_denied_payload.dart';
import '../protocol/auth_payload.dart';
import '../protocol/auth_ok_payload.dart';
import '../protocol/disconnect_payload.dart';
import '../protocol/error_payload.dart';
import '../protocol/frame.dart';
import '../protocol/frame_builder.dart';
import '../protocol/hello_payload.dart';
import '../protocol/heartbeat_payload.dart';
import '../protocol/input_event.dart';
import '../protocol/input_event_payload.dart';
import '../protocol/input_reset_payload.dart';
import '../protocol/input_snapshot_payload.dart';
import '../protocol/message_types.dart';
import '../protocol/pong_payload.dart';
import '../protocol/welcome_payload.dart';
import '../transport/transport_connection.dart';
import 'ack_tracker.dart';
import 'authenticator.dart';
import 'token_store.dart';
import 'input_snapshot_provider.dart';
import 'sequence_tracker.dart';
import 'session_listener.dart';
import 'session_state.dart';

/// Client-side CTRL session (docs/protocol.md §19). Requires the transport to
/// be connected BEFORE connect() (the transport contract has no connect()).
///
/// Flow: HELLO(seq 0) -> WELCOME -> AUTH(seq 1) -> AUTH_OK -> ready, then the
/// first INPUT_SNAPSHOT(seq 2) is sent from [InputSnapshotProvider] and
/// re-sent on every INPUT_RESET. Reconnect is manual only (D8).
class ClientSession {
  ClientSession({
    required this.transport,
    required this.authenticator,
    required this.listener,
    required this.inputSnapshotProvider,
    TokenStore? tokenStore,
    this.protocolMajor = 1,
    this.protocolMinor = 0,
    this.ackTimeoutMs = 3000,
    int Function()? nowMs,
  })  : tokenStore = tokenStore ?? InMemoryTokenStore(),
        nowMs = nowMs ?? _defaultNowMs {
    _ackTracker = AckTracker(nowMs: this.nowMs, timeoutMs: ackTimeoutMs);
  }

  final TransportConnection transport;
  final HmacAuthenticator authenticator;
  final SessionListener listener;
  final InputSnapshotProvider inputSnapshotProvider;

  /// Receives newToken issued on pairing success (docs/protocol.md §12).
  /// Production apps pass a secure store; the default keeps tokens in memory.
  final TokenStore tokenStore;
  final int protocolMajor;
  final int protocolMinor;
  final int ackTimeoutMs;
  final int Function() nowMs;

  SequenceTracker _outbound = SequenceTracker();
  InboundSequenceTracker _inbound = InboundSequenceTracker();
  late AckTracker _ackTracker;
  final Map<int, _PendingSend> _pendingSends = <int, _PendingSend>{};
  Future<void> _sendTail = Future<void>.value();

  ClientSessionState _state = ClientSessionState.closed;
  ClientSessionState get state => _state;

  Uint8List? _welcomeSessionId;
  int _effectiveMajor = 1;
  bool _deliberateClose = false;
  StreamSubscription<ProtocolFrame>? _framesSub;
  StreamSubscription<String>? _disconnectedSub;

  /// Starts a session handshake. Throws unless the transport is already
  /// connected and the session is in a connectable state.
  void connect() {
    if (!transport.isConnected) {
      throw StateError('connect() requires the transport to be connected.');
    }
    if (_state != ClientSessionState.closed &&
        _state != ClientSessionState.disconnected) {
      throw StateError('Session is not in a connectable state ($_state).');
    }

    _ackTracker = AckTracker(nowMs: nowMs, timeoutMs: ackTimeoutMs);
    _outbound = SequenceTracker();
    _welcomeSessionId = null;
    _deliberateClose = false;
    _effectiveMajor = protocolMajor;

    _framesSub = transport.frames.listen(_onFrame);
    _disconnectedSub = transport.disconnected.listen(_onDisconnected);
    _setState(ClientSessionState.connecting);
    _setState(ClientSessionState.connected);
    _enqueueHello();
  }

  /// Manual reconnect (D8): requires [ClientSessionState.reconnecting] and a
  /// transport that has been reconnected by the caller.
  void reconnect() {
    if (_state != ClientSessionState.reconnecting) {
      throw StateError('reconnect() requires the reconnecting state ($_state).');
    }
    if (!transport.isConnected) {
      throw StateError('reconnect() requires the transport to be reconnected.');
    }

    _framesSub?.cancel();
    _disconnectedSub?.cancel();
    _ackTracker = AckTracker(nowMs: nowMs, timeoutMs: ackTimeoutMs);
    _outbound = SequenceTracker();
    _welcomeSessionId = null;
    _deliberateClose = false;
    _effectiveMajor = protocolMajor;

    _framesSub = transport.frames.listen(_onFrame);
    _disconnectedSub = transport.disconnected.listen(_onDisconnected);
    _setState(ClientSessionState.connecting);
    _setState(ClientSessionState.connected);
    _enqueueHello();
  }

  /// Deliberately ends the session.
  Future<void> close() async {
    if (_state == ClientSessionState.closed) {
      return;
    }
    _deliberateClose = true;
    _setState(ClientSessionState.disconnected);
    await _framesSub?.cancel();
    await _disconnectedSub?.cancel();
    try {
      await transport.close();
    } catch (_) {}
  }

  /// True once every queued outbound frame has been flushed. Tests await this
  /// after driving inbound frames to make assertions deterministic.
  Future<void> waitForIdle() => _sendTail;

  /// Simulates the ACK keepalive tick: retry-once, then fail (D7).
  void processPendingAcks() {
    for (final sequence in _ackTracker.retryExpired()) {
      _retransmit(sequence);
    }
    for (final sequence in _ackTracker.failed()) {
      _pendingSends.remove(sequence);
      listener.onError(
          'Message sequence $sequence unacknowledged after retry; session unhealthy.');
      _closeDeliberate();
    }
  }

  /// Sends one input event on the hot path. Only allowed while READY; before
  /// authentication the protocol forbids application-plane messages (§3).
  void sendInputEvent(InputEvent event) {
    if (_state != ClientSessionState.ready) {
      throw StateError('sendInputEvent() requires the ready state ($_state).');
    }
    _enqueue(
      type: MessageType.inputEvent,
      payload: InputEventPayloadCodec.encode(InputEventPayload(event: event)),
    );
  }

  /// Sends a HEARTBEAT carrying the current clientSendTime (docs/protocol.md
  /// §10; client interval 1000 ms). The periodic timer is deferred (D7) — the
  /// caller drives this, which keeps tests deterministic.
  void sendHeartbeat() {
    if (_state != ClientSessionState.ready) {
      throw StateError('sendHeartbeat() requires the ready state ($_state).');
    }
    _enqueue(
      type: MessageType.heartbeat,
      payload:
          HeartbeatPayloadCodec.encode(HeartbeatPayload(clientSendTime: nowMs())),
    );
  }

  /// Sends a control-plane message that requests an ACK (docs/protocol.md §11,
  /// e.g. CONFIG_PUSH). The ACK is tracked with retry-once semantics.
  void sendWithAck({required int type, required Uint8List payload}) {
    if (_state != ClientSessionState.ready) {
      throw StateError('sendWithAck() requires the ready state ($_state).');
    }
    _enqueue(
      type: type,
      payload: payload,
      mustUnderstand: true,
      ackRequested: true,
    );
  }

  /// Graceful teardown (§14): sends DISCONNECT, waits for the frame to flush,
  /// then closes the session.
  Future<void> sendDisconnect({int reason = disconnectReasonNormal}) async {
    if (_state == ClientSessionState.closed ||
        _state == ClientSessionState.disconnected) {
      return;
    }
    _enqueue(
      type: MessageType.disconnect,
      payload:
          DisconnectPayloadCodec.encode(DisconnectPayload(reason: reason)),
      mustUnderstand: true,
    );
    await waitForIdle();
    await _closeDeliberate();
  }

  void _enqueueHello() {
    final hello = HelloPayload(
      deviceId: authenticator.deviceId,
      clientVersion: '0.1.0',
      protocolMajor: protocolMajor,
      protocolMinor: protocolMinor,
      capabilities: 0x00000007,
    );
    _enqueue(
      type: MessageType.hello,
      payload: HelloPayloadCodec.encode(hello),
      mustUnderstand: true,
    );
    _setState(ClientSessionState.waitWelcome);
  }

  void _onFrame(ProtocolFrame frame) {
    if (_state == ClientSessionState.closed ||
        _state == ClientSessionState.disconnected) {
      return;
    }

    if ((frame.flags & FrameCodec.reservedFlagsMask) != 0) {
      _sendErrorAndClose(
        errorCodeForbidden,
        'Reserved frame flags are forbidden in protocol v1.',
      );
      return;
    }

    if (frame.versionMajor != _effectiveMajor) {
      if (frame.messageType == MessageType.welcome ||
          frame.messageType == MessageType.hello) {
        _sendErrorAndClose(
          errorCodeProtocolVersionMismatch,
          'Unsupported protocol version ${frame.versionMajor}.${frame.versionMinor}.',
        );
      } else {
        _closeDeliberate();
      }
      return;
    }

    if (!_inbound.isMonotonic(frame.sequence)) {
      listener.onError(
          'Non-monotonic inbound sequence ${frame.sequence} (recorded; not fatal).');
    }

    if ((frame.flags & FrameCodec.ackRequested) != 0) {
      _enqueueAck(frame.sequence);
    }

    switch (frame.messageType) {
      case MessageType.welcome:
        _onWelcome(frame);
        break;
      case MessageType.authOk:
        _onAuthOk(frame);
        break;
      case MessageType.authDenied:
        _onAuthDenied(frame);
        break;
      case MessageType.inputReset:
        _onInputReset(frame);
        break;
      case MessageType.pong:
        _onPong(frame);
        break;
      case MessageType.ack:
        _onAck(frame);
        break;
      case MessageType.error:
        _onError(frame);
        break;
      case MessageType.disconnect:
        _onDisconnectMessage(frame);
        break;
      default:
        _onOther(frame);
        break;
    }
  }

  void _onWelcome(ProtocolFrame frame) {
    if (_state != ClientSessionState.waitWelcome) {
      _sendErrorAndClose(errorCodeInvalidMessage, 'WELCOME received out of state.');
      return;
    }
    final WelcomePayload welcome;
    try {
      welcome = WelcomePayloadCodec.decode(frame.payload);
    } on ProtocolException catch (e) {
      _sendErrorAndClose(errorCodeInvalidMessage, 'Malformed WELCOME: ${e.message}');
      return;
    }

    if (welcome.effectiveMajor != protocolMajor) {
      _sendErrorAndClose(
        errorCodeProtocolVersionMismatch,
        'Unsupported protocol version ${welcome.effectiveMajor}.${welcome.effectiveMinor}.',
      );
      return;
    }

    _effectiveMajor = welcome.effectiveMajor;
    _welcomeSessionId = Uint8List.fromList(welcome.sessionId);
    _setState(ClientSessionState.waitAuth);

    if (!welcome.authRequired) {
      // Not needed in v1 (D6: server always requires auth), but keep the branch.
      _becomeReady();
      return;
    }

    final auth = authenticator.buildAuth(welcome.challenge);
    _enqueue(
      type: MessageType.auth,
      payload: AuthPayloadCodec.encode(auth),
      mustUnderstand: true,
    );
    _setState(ClientSessionState.waitAuthOk);
  }

  void _onAuthOk(ProtocolFrame frame) {
    if (_state != ClientSessionState.waitAuthOk) {
      _sendErrorAndClose(errorCodeInvalidMessage, 'AUTH_OK received out of state.');
      return;
    }
    final AuthOkPayload authOk;
    try {
      authOk = AuthOkPayloadCodec.decode(frame.payload);
    } on ProtocolException catch (e) {
      _sendErrorAndClose(errorCodeInvalidMessage, 'Malformed AUTH_OK: ${e.message}');
      return;
    }

    if (_welcomeSessionId == null ||
        !_bytesEqual(authOk.sessionId, _welcomeSessionId!)) {
      _sendErrorAndClose(
        errorCodeInvalidMessage,
        'AUTH_OK sessionId does not match WELCOME sessionId.',
      );
      return;
    }

    // §12: pairing success carries a newToken the client must store.
    if (authOk.newToken.isNotEmpty) {
      final token = Uint8List.fromList(authOk.newToken);
      final store = tokenStore;
      unawaited(store
          .save(authenticator.deviceId, token)
          .catchError((Object e) {
        // Never include token material in diagnostics.
        listener.onError('Failed to persist newToken: $e');
      }));
    }

    _becomeReady();
  }

  void _becomeReady() {
    // docs/protocol.md §7/§24.5: the per-direction sequence counter starts
    // from 0 exactly after AUTH_OK — reset both directions at this boundary.
    _outbound = SequenceTracker();
    _inbound = InboundSequenceTracker();
    _setState(ClientSessionState.ready);
    _enqueueSnapshot();
  }

  void _onAuthDenied(ProtocolFrame frame) {
    if (_state != ClientSessionState.waitAuthOk) {
      _sendErrorAndClose(errorCodeInvalidMessage, 'AUTH_DENIED received out of state.');
      return;
    }
    try {
      AuthDeniedPayloadCodec.decode(frame.payload);
    } on ProtocolException {
      // Malformed AUTH_DENIED is still an end-of-session signal.
    }
    listener.onError('Authentication denied by server.');
    _closeDeliberate();
  }

  void _onInputReset(ProtocolFrame frame) {
    if (_state != ClientSessionState.ready) {
      _sendErrorAndClose(errorCodeInvalidMessage, 'INPUT_RESET received out of state.');
      return;
    }
    try {
      InputResetPayloadCodec.decode(frame.payload);
    } on ProtocolException catch (e) {
      listener.onError('Dropped malformed INPUT_RESET payload: ${e.message}');
      return;
    }
    _enqueueSnapshot();
  }

  void _onPong(ProtocolFrame frame) {
    if (_state != ClientSessionState.ready) {
      _sendErrorAndClose(errorCodeInvalidMessage, 'PONG received out of state.');
      return;
    }
    try {
      listener.onPong(PongPayloadCodec.decode(frame.payload));
    } on ProtocolException catch (e) {
      listener.onError('Dropped malformed PONG payload: ${e.message}');
    }
  }

  void _onAck(ProtocolFrame frame) {
    final AckPayload ack;
    try {
      ack = AckPayloadCodec.decode(frame.payload);
    } on ProtocolException catch (e) {
      listener.onError('Dropped malformed ACK payload: ${e.message}');
      return;
    }
    _ackTracker.acknowledge(ack.ackedSequence);
    _pendingSends.remove(ack.ackedSequence);
  }

  void _onError(ProtocolFrame frame) {
    final ErrorPayload error;
    try {
      error = ErrorPayloadCodec.decode(frame.payload);
    } on ProtocolException catch (e) {
      listener.onError('Dropped malformed ERROR payload: ${e.message}');
      return;
    }
    listener.onError('Peer sent ERROR 0x${error.code.toRadixString(16)} (${error.message}).');
    if (error.severity == errorSeverityFatal) {
      listener.onFatalError(error);
      _closeDeliberate();
    }
  }

  void _onDisconnectMessage(ProtocolFrame frame) {
    try {
      DisconnectPayloadCodec.decode(frame.payload);
    } on ProtocolException {
      // Malformed DISCONNECT is still an end-of-session signal.
    }
    _closeDeliberate();
  }

  void _onOther(ProtocolFrame frame) {
    final type = frame.messageType;

    // Known client→server types that a server must never send to a client.
    if (type == MessageType.hello ||
        type == MessageType.auth ||
        type == MessageType.inputEvent ||
        type == MessageType.inputSnapshot ||
        type == MessageType.heartbeat ||
        type == MessageType.profileListReq ||
        type == MessageType.profileSelect ||
        type == MessageType.configPush) {
      _sendErrorAndClose(
        errorCodeInvalidMessage,
        'Message type 0x${type.toRadixString(16)} is not valid from server to client.',
      );
      return;
    }

    if ((frame.flags & FrameCodec.mustUnderstand) != 0) {
      _sendErrorAndClose(
        errorCodeUnsupportedMessage,
        'Unsupported message type 0x${type.toRadixString(16)}.',
      );
    } else {
      listener.onError(
          'Ignored unsupported message type 0x${type.toRadixString(16)} (MUST_UNDERSTAND not set).');
    }
  }

  void _onDisconnected(String reason) {
    if (_deliberateClose) {
      _setState(ClientSessionState.disconnected);
      return;
    }
    listener.onError('Connection lost: $reason');
    _setState(ClientSessionState.reconnecting);
  }

  void _enqueueSnapshot() {
    if (_state != ClientSessionState.ready) {
      return;
    }
    final snapshot = inputSnapshotProvider.currentSnapshot();
    _enqueue(
      type: MessageType.inputSnapshot,
      payload: InputSnapshotPayloadCodec.encode(snapshot),
    );
  }

  void _enqueueAck(int ackedSequence) {
    _enqueue(
      type: MessageType.ack,
      payload: AckPayloadCodec.encode(AckPayload(
        ackedSequence: ackedSequence,
        ackTime: nowMs(),
      )),
    );
  }

  void _sendErrorAndClose(int code, String message) {
    _enqueue(
      type: MessageType.error,
      payload: ErrorPayloadCodec.encode(ErrorPayload(
        code: code,
        severity: errorSeverityFatal,
        message: message,
      )),
      mustUnderstand: true,
    );
    _closeDeliberate();
  }

  void _enqueue({
    required int type,
    required Uint8List payload,
    bool mustUnderstand = false,
    bool ackRequested = false,
    bool trackAck = true,
  }) {
    _enqueueWithSequence(
      _outbound.next(),
      type: type,
      payload: payload,
      mustUnderstand: mustUnderstand,
      ackRequested: ackRequested,
      trackAck: trackAck,
    );
  }

  void _enqueueWithSequence(
    int sequence, {
    required int type,
    required Uint8List payload,
    bool mustUnderstand = false,
    bool ackRequested = false,
    bool trackAck = true,
  }) {
    final bytes = FrameBuilder.build(
      messageType: type,
      payload: payload,
      sequence: sequence,
      ackRequested: ackRequested,
      mustUnderstand: mustUnderstand,
      versionMajor: _effectiveMajor,
      versionMinor: protocolMinor,
      timestamp: nowMs(),
    );

    if (ackRequested && trackAck) {
      _ackTracker.track(sequence);
      _pendingSends[sequence] =
          _PendingSend(type: type, payload: payload, mustUnderstand: mustUnderstand);
    }

    _sendTail = _sendTail.then((_) => _transportSend(bytes));
  }

  void _retransmit(int sequence) {
    final pending = _pendingSends[sequence];
    if (pending == null) {
      return;
    }
    listener.onError('Retransmitting sequence $sequence after ACK timeout.');
    // Re-send the SAME sequence with ACK_REQUESTED but do NOT re-track: the
    // pending entry keeps its attempt count so the retry fires once and the
    // next deadline triggers failure (docs/protocol.md §7).
    _enqueueWithSequence(
      sequence,
      type: pending.type,
      payload: pending.payload,
      mustUnderstand: pending.mustUnderstand,
      ackRequested: true,
      trackAck: false,
    );
    _ackTracker.reschedule(sequence);
  }

  Future<void> _transportSend(Uint8List bytes) async {
    try {
      await transport.send(bytes);
    } catch (e) {
      listener.onError('Send failed: $e');
    }
  }

  Future<void> _closeDeliberate() async {
    if (_deliberateClose) {
      return;
    }
    _deliberateClose = true;
    _setState(ClientSessionState.disconnected);
    await _framesSub?.cancel();
    await _disconnectedSub?.cancel();
    try {
      await transport.close();
    } catch (_) {}
  }

  void _setState(ClientSessionState state) {
    if (_state == state) {
      return;
    }
    _state = state;
    listener.onStateChanged(state);
  }

  static int _defaultNowMs() => DateTime.now().millisecondsSinceEpoch;
}

class _PendingSend {
  _PendingSend({required this.type, required this.payload, required this.mustUnderstand});

  final int type;
  final Uint8List payload;
  final bool mustUnderstand;
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