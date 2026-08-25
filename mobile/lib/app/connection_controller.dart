import 'package:flutter/foundation.dart';

import '../input/keyboard_bindings.dart';
import '../protocol/error_payload.dart';
import '../protocol/input_event.dart';
import '../protocol/input_snapshot_payload.dart';
import '../session/authenticator.dart';
import '../session/client_session.dart';
import '../session/input_snapshot_provider.dart';
import '../session/session_listener.dart';
import '../session/session_state.dart';
import '../session/token_store.dart';
import '../transport/tcp_transport.dart';
import '../transport/transport_connection.dart';
import 'app_connection_phase.dart';

/// Signature for creating an ALREADY-CONNECTED transport. Injectable so tests
/// can drive the real [ClientSession] against a scripted fake transport.
typedef TransportFactory = Future<TransportConnection> Function(String host, int port);

/// Application-layer boundary between Flutter widgets and the CTRL engine
/// (M3.0 Phase 1).
///
/// Owns one [ClientSession] at a time, projects engine states onto
/// [AppConnectionPhase], and routes input events. Widgets never touch
/// protocol/transport/authentication types.
///
/// Security notes:
///  - tokens live only inside the injected [TokenStore] (SecureTokenStore in
///    production); they are never exposed to widgets or logs;
///  - error strings surfaced to the UI come from the engine and never contain
///    credential material by design.
class ConnectionController extends ChangeNotifier {
  ConnectionController({TokenStore? tokenStore})
      : tokenStore = tokenStore ?? InMemoryTokenStore();

  /// Production token storage (SecureTokenStore) is provided by main.dart.
  final TokenStore tokenStore;

  /// Overridable for tests; production uses the real TCP transport.
  @visibleForTesting
  TransportFactory transportFactory =
      (host, port) async {
        final transport = TcpTransport(host: host, port: port);
        await transport.connect();
        return transport;
      };

  ClientSession? _session;
  TransportConnection? _transport;
  bool _pairingMode = false;

  AppConnectionPhase _phase = AppConnectionPhase.disconnected;
  String? _lastError;
  final Map<String, int> _pressCounts = {};

  AppConnectionPhase get phase => _phase;
  String? get lastError => _lastError;

  /// True when a stored token exists on this device — pairing code entry is
  /// only required when false.
  Future<bool> hasStoredToken(String deviceId) async =>
      await tokenStore.load(deviceId) != null;

  void sendKeyEvent(String keyName, bool down) {
    final session = _session;
    if (session == null || _phase != AppConnectionPhase.connected) {
      return;
    }
    if (!isSupportedKeyName(keyName)) {
      return;
    }
    final controlId = keyboardControlId(keyName);
    final pressCount = down ? (_pressCounts[controlId] ?? 0) + 1 : (_pressCounts[controlId] ?? 1);
    if (down) {
      _pressCounts[controlId] = pressCount;
    }
    session.sendInputEvent(InputEvent(
      controlId: controlId,
      kind: inputEventKindButton,
      flags: inputEventFlagStateChanged,
      state: down ? inputEventStateDown : inputEventStateUp,
      pressCount: pressCount,
    ));
  }

  /// Starts a connection attempt.
  ///
  /// Uses the stored token when present; otherwise requires a [pairingCode].
  Future<void> connect({
    required String host,
    required int port,
    required String deviceId,
    String? pairingCode,
  }) async {
    assert(port > 0 && port <= 65535);
    await teardown();
    _lastError = null;

    final storedToken = await tokenStore.load(deviceId);
    final HmacAuthenticator authenticator;
    if (storedToken != null) {
      authenticator = HmacAuthenticator.token(token: storedToken, deviceId: deviceId);
      _pairingMode = false;
    } else {
      final code = pairingCode?.trim() ?? '';
      if (code.isEmpty) {
        _setError('Pairing code required for first-time setup.');
        return;
      }
      authenticator = HmacAuthenticator.pairing(pairingCode: code, deviceId: deviceId);
      _pairingMode = true;
    }

    _setPhase(storedToken != null || pairingCode == null
        ? AppConnectionPhase.connecting
        : AppConnectionPhase.pairing);

    try {
      final transport = await transportFactory(host, port);
      _transport = transport;
      _session = ClientSession(
        transport: transport,
        authenticator: authenticator,
        listener: _SessionListenerBridge(this),
        inputSnapshotProvider: const _AppSnapshotProvider(),
        tokenStore: tokenStore,
      );
      // Engine drives the handshake from here; state changes arrive via the
      // listener bridge below.
      _session!.connect();
    } catch (e) {
      await teardown();
      _setError('Could not reach server: $e');
    }
  }

  /// Graceful disconnect when connected; hard teardown otherwise.
  Future<void> disconnect() async {
    final session = _session;
    if (session != null &&
        session.state != ClientSessionState.closed &&
        session.state != ClientSessionState.disconnected) {
      try {
        await session.sendDisconnect();
      } catch (_) {
        // Best-effort: teardown below still runs.
      }
    }
    await teardown();
    _setPhase(AppConnectionPhase.disconnected);
  }

  /// Returns to a clean disconnected state after reconnecting/error phases
  /// (D8 manual reconnect with a brand-new session happens via [connect]).
  Future<void> resetAfterFailure() async {
    await disconnect();
  }

  /// Tears down the current session/transport without changing phase unless
  /// it was active. Never throws.
  @visibleForTesting
  Future<void> teardown() async {
    final session = _session;
    _session = null;
    final transport = _transport;
    _transport = null;
    _pressCounts.clear();
    if (session != null) {
      try {
        await session.sendDisconnect();
      } catch (_) {}
    }
    if (transport != null) {
      try {
        await transport.close();
      } catch (_) {}
    }
  }

  /// Test seam: force a phase for widget rendering tests.
  @visibleForTesting
  void debugSetPhase(AppConnectionPhase phase) => _setPhase(phase);

  /// Test seam: drains the session's pending send queue.
  @visibleForTesting
  Future<void> waitForIdleForTest() => _session?.waitForIdle() ?? Future.value();

  void _setPhase(AppConnectionPhase phase) {
    if (_phase == phase && phase != AppConnectionPhase.error) {
      return;
    }
    _phase = phase;
    notifyListeners();
  }

  void _setError(String message) {
    _lastError = message;
    _setPhase(AppConnectionPhase.error);
  }
}

class _SessionListenerBridge implements SessionListener {
  _SessionListenerBridge(this._controller);

  final ConnectionController _controller;

  @override
  void onStateChanged(ClientSessionState state) {
    switch (state) {
      case ClientSessionState.waitWelcome:
      case ClientSessionState.waitAuth:
      case ClientSessionState.waitAuthOk:
        _controller._setPhase(_controller._pairingMode
            ? AppConnectionPhase.pairing
            : AppConnectionPhase.connecting);
        break;
      case ClientSessionState.ready:
        _controller._lastError = null;
        _controller._setPhase(AppConnectionPhase.connected);
        break;
      case ClientSessionState.reconnecting:
        _controller._setPhase(AppConnectionPhase.reconnecting);
        break;
      case ClientSessionState.connected:
      case ClientSessionState.connecting:
        break; // transient; waitWelcome arrives immediately after
      case ClientSessionState.closed:
      case ClientSessionState.disconnected:
        // Terminal states surface through onError/phase already set by the
        // controller paths; keep the current phase unless nothing was set.
        if (_controller._phase == AppConnectionPhase.connecting ||
            _controller._phase == AppConnectionPhase.pairing) {
          _controller._setPhase(AppConnectionPhase.error);
        }
        break;
    }
  }

  @override
  void onError(String message) {
    // The engine guarantees diagnostics never contain credential material.
    if (message.contains('Authentication denied')) {
      _controller._lastError = _controller._pairingMode
          ? 'Pairing rejected by desktop (wrong or used/expired pairing code).'
          : 'Authentication denied by desktop.';
      _controller._setPhase(AppConnectionPhase.error);
      return;
    }
    // Non-fatal diagnostics during READY do not flip the connection phase.
  }

  @override
  void onFatalError(ErrorPayload error) {
    _controller._lastError =
        'Desktop error 0x${error.code.toRadixString(16)}: ${error.message}';
    _controller._setPhase(AppConnectionPhase.error);
  }

  @override
  void onPong(pong) {}
}

/// Minimal snapshot provider: reports a neutral single button so the mandatory
/// post-AUTH_OK INPUT_SNAPSHOT satisfies codec rules (1..1024 entries).
class _AppSnapshotProvider implements InputSnapshotProvider {
  const _AppSnapshotProvider();

  @override
  InputSnapshotPayload currentSnapshot() => InputSnapshotPayload(events: [
        const InputEvent(
          controlId: 'app',
          kind: inputEventKindButton,
          flags: inputEventFlagInitial,
          state: inputEventStateUp,
          pressCount: 0,
        ),
      ]);
}
