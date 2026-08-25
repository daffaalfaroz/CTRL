import 'dart:async';

import 'package:flutter/foundation.dart';

import '../input/keyboard_bindings.dart';
import '../input/mouse_bindings.dart';
import '../input/touchpad_controller.dart';
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
import 'connection_settings.dart';

/// Signature for creating an ALREADY-CONNECTED transport. Injectable so tests
/// can drive the real [ClientSession] against a scripted fake transport.
typedef TransportFactory = Future<TransportConnection> Function(String host, int port);

/// Application-layer boundary between Flutter widgets and the CTRL engine
/// (M3.0). Owns one [ClientSession] at a time, projects engine states onto
/// [AppConnectionPhase], routes input events, persists connection settings,
/// and orchestrates bounded automatic reconnection after unexpected drops.
///
/// Security notes:
///  - tokens live only inside the injected [TokenStore] (SecureTokenStore in
///    production); they are never exposed to widgets or logs;
///  - persisted settings exclude secrets (pairing codes / tokens);
///  - error strings surfaced to the UI come from the engine and never contain
///    credential material by design.
class ConnectionController extends ChangeNotifier {
  ConnectionController({TokenStore? tokenStore, ConnectionSettingsStore? settingsStore})
      : tokenStore = tokenStore ?? InMemoryTokenStore(),
        settingsStore = settingsStore ?? ConnectionSettingsStore();

  /// Production token storage (SecureTokenStore) is provided by main.dart.
  final TokenStore tokenStore;

  /// Non-secret connection settings persistence (Phase 2).
  final ConnectionSettingsStore settingsStore;

  /// Overridable for tests; production uses the real TCP transport.
  @visibleForTesting
  TransportFactory transportFactory = (host, port) async {
    final transport = TcpTransport(host: host, port: port);
    await transport.connect();
    return transport;
  };

  /// Bounded reconnect backoff schedule. Deterministic for tests.
  @visibleForTesting
  static const List<Duration> reconnectBackoff = [
    Duration(milliseconds: 500),
    Duration(seconds: 1),
    Duration(seconds: 2),
    Duration(seconds: 4),
    Duration(seconds: 8),
  ];

  /// Overridable delay runner so reconnect tests run instantly.
  @visibleForTesting
  Future<void> Function(Duration delay) delayRunner =
      (Duration delay) => Future<void>.delayed(delay);

  ClientSession? _session;
  TransportConnection? _transport;
  bool _pairingMode = false;

  AppConnectionPhase _phase = AppConnectionPhase.disconnected;
  String? _lastError;
  final Map<String, int> _pressCounts = {};
  final Set<String> _heldMouseButtons = {};

  // Auto-reconnect orchestration state.
  int _generation = 0;
  bool _autoReconnectActive = false;
  bool _suppressAutoReconnect = false;
  bool _authDeniedSinceLastConnect = false;
  @visibleForTesting
  int reconnectAttempts = 0;
  ConnectionSettings? _lastSettings;

  AppConnectionPhase get phase => _phase;
  String? get lastError => _lastError;

  /// True when a stored token exists on this device — pairing code entry is
  /// only required when false.
  Future<bool> hasStoredToken(String deviceId) async =>
      await tokenStore.load(deviceId) != null;

  void sendKeyEvent(String keyName, bool down) {
    final session = _readySession();
    if (session == null || !isSupportedKeyName(keyName)) {
      return;
    }
    final controlId = keyboardControlId(keyName);
    final pressCount =
        down ? (_pressCounts[controlId] ?? 0) + 1 : (_pressCounts[controlId] ?? 1);
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

  /// Relative cursor velocity from the touchpad (-1..1 per axis, §9 range).
  void sendMouseMove(double vx, double vy) {
    _readySession()?.sendInputEvent(InputEvent(
      controlId: mouseMoveControlId,
      kind: inputEventKindStick,
      flags: inputEventFlagStateChanged,
      x: vx.clamp(-1.0, 1.0).toDouble(),
      y: vy.clamp(-1.0, 1.0).toDouble(),
    ));
  }

  void sendMouseButton(String controlId, bool down) {
    final session = _readySession();
    if (session == null || !supportedMouseButtons.contains(controlId)) {
      return;
    }
    final pressKey = 'mouse:$controlId';
    final pressCount =
        down ? (_pressCounts[pressKey] ?? 0) + 1 : (_pressCounts[pressKey] ?? 1);
    if (down) {
      _pressCounts[pressKey] = pressCount;
      _heldMouseButtons.add(controlId);
    } else {
      _heldMouseButtons.remove(controlId);
    }
    session.sendInputEvent(InputEvent(
      controlId: controlId,
      kind: inputEventKindButton,
      flags: inputEventFlagStateChanged,
      state: down ? inputEventStateDown : inputEventStateUp,
      pressCount: pressCount,
    ));
  }

  /// Wheel scroll rate for one direction (0..1 speed, §9-normalized axis).
  void sendWheel({required bool up, required double speed}) {
    _readySession()?.sendInputEvent(InputEvent(
      controlId: up ? mouseWheelUpControlId : mouseWheelDownControlId,
      kind: inputEventKindAxis,
      flags: inputEventFlagStateChanged,
      value: speed.clamp(0.0, 1.0).toDouble(),
    ));
  }

  /// Routes a semantic [TouchpadAction] produced by the gesture interpreter.
  void handleTouchpadAction(TouchpadAction action) {
    switch (action.type) {
      case TouchpadActionType.moveVelocity:
        sendMouseMove(action.vx, action.vy);
        break;
      case TouchpadActionType.button:
        sendMouseButton(action.buttonId!, action.down);
        break;
      case TouchpadActionType.wheelVelocity:
        sendWheel(up: action.wheelUp, speed: action.vx);
        break;
    }
  }

  ClientSession? _readySession() {
    if (_phase != AppConnectionPhase.connected) {
      return null; // dead/stale sessions must not emit input (M3.0 rule)
    }
    return _session;
  }

  /// Starts a connection attempt.
  ///
  /// Uses the stored token when present; otherwise requires a [pairingCode].
  /// Persists the non-secret settings for future launches (Phase 2).
  Future<void> connect({
    required String host,
    required int port,
    required String deviceId,
    String? pairingCode,
  }) async {
    assert(port > 0 && port <= 65535);
    _generation++; // invalidates any in-flight auto-reconnect loop
    _suppressAutoReconnect = false;
    _authDeniedSinceLastConnect = false;
    reconnectAttempts = 0;
    await teardown();
    _lastError = null;

    final settings =
        ConnectionSettings(host: host, port: port, deviceId: deviceId);
    _lastSettings = settings;
    try {
      await settingsStore.save(settings);
    } catch (_) {
      // Persistence failure is non-fatal; connection still proceeds.
    }

    final storedToken = await tokenStore.load(deviceId);
    final HmacAuthenticator authenticator;
    if (storedToken != null) {
      authenticator =
          HmacAuthenticator.token(token: storedToken, deviceId: deviceId);
      _pairingMode = false;
    } else {
      final code = pairingCode?.trim() ?? '';
      if (code.isEmpty) {
        _setError('Pairing code required for first-time setup.');
        return;
      }
      authenticator =
          HmacAuthenticator.pairing(pairingCode: code, deviceId: deviceId);
      _pairingMode = true;
    }

    _setPhase(storedToken != null
        ? AppConnectionPhase.connecting
        : AppConnectionPhase.pairing);

    var established = false;
    try {
      established = await _establish(settings, authenticator);
    } catch (e) {
      await teardown();
      // Transport-level failure is transient-classified: hand over to the
      // bounded auto-reconnect loop instead of a terminal error.
      _startAutoReconnectIfEligible();
      if (!_autoReconnectActive) {
        _setError('Could not reach server: $e');
      } else {
        _setPhase(AppConnectionPhase.reconnecting);
        _lastError = 'Reconnecting…';
      }
      return;
    }
    if (!established) {
      return; // terminal error already surfaced by validation paths
    }
  }

  /// Graceful disconnect when connected; hard teardown otherwise. Explicit
  /// user intent SUPPRESSES automatic reconnection (M3.0 rule).
  Future<void> disconnect() async {
    _generation++;
    _suppressAutoReconnect = true;
    _autoReconnectActive = false;
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

  /// Returns to a clean disconnected state after reconnecting/error phases.
  Future<void> resetAfterFailure() => disconnect();

  @override
  void dispose() {
    _generation++;
    _autoReconnectActive = false;
    _disposed = true;
    final transport = _transport;
    _transport = null;
    _session = null;
    try {
      transport?.close();
    } catch (_) {}
    super.dispose();
  }

  bool _disposed = false;

  // --- internals ------------------------------------------------------------

  /// Builds transport + session and starts the handshake. Returns true when
  /// the handshake is in flight; throws on transport failure.
  Future<bool> _establish(
      ConnectionSettings settings, HmacAuthenticator authenticator) async {
    final storedToken = await tokenStore.load(settings.deviceId);
    if (storedToken == null && !_pairingMode) {
      return false; // no credential available (permanent)
    }
    final transport = await transportFactory(settings.host, settings.port);
    _transport = transport;
    _session = ClientSession(
      transport: transport,
      authenticator: authenticator,
      listener: _SessionListenerBridge(this),
      inputSnapshotProvider: const _AppSnapshotProvider(),
      tokenStore: tokenStore,
    );
    _session!.connect();
    return true;
  }

  void _startAutoReconnectIfEligible() {
    if (_disposed ||
        _suppressAutoReconnect ||
        _authDeniedSinceLastConnect ||
        _autoReconnectActive ||
        _lastSettings == null) {
      return;
    }
    _autoReconnectActive = true;
    // Fire-and-forget: the loop self-cancels via generation checks.
    unawaited(_runAutoReconnect());
  }

  /// Unexpected drop while previously connected: enter reconnecting and hand
  /// over to the bounded auto-reconnect loop (token-only; no pairing replay).
  void _onUnexpectedDrop() {
    _setPhase(AppConnectionPhase.reconnecting);
    _startAutoReconnectIfEligible();
  }

  Future<void> _runAutoReconnect() async {
    final generation = _generation;
    final cfg = _lastSettings!;
    for (final delay in reconnectBackoff) {
      if (_generation != generation || _disposed || _suppressAutoReconnect) {
        return;
      }
      _setPhase(AppConnectionPhase.reconnecting);
      await delayRunner(delay);
      if (_generation != generation || _disposed || _suppressAutoReconnect) {
        return;
      }

      final storedToken = await tokenStore.load(cfg.deviceId);
      if (storedToken == null) {
        // Pairing never completed or storage cleared: permanent for auto flow.
        _setError('Stored credentials missing; pair again.');
        return;
      }
      if (_authDeniedSinceLastConnect) {
        // Bridge already surfaced the sanitized error; do not hammer the
        // desktop lockout window.
        return;
      }

      reconnectAttempts++;
      _pairingMode = false;
      _setPhase(AppConnectionPhase.reconnecting);
      var established = true;
      try {
        established = await _establish(
            cfg, HmacAuthenticator.token(token: storedToken, deviceId: cfg.deviceId));
      } catch (_) {
        established = false; // transient network failure class
      }
      if (!established) {
        continue; // next backoff step
      }
      if (_generation != generation || _disposed || _suppressAutoReconnect) {
        return; // superseded mid-attempt by a newer user action
      }
      _autoReconnectActive = false;
      return; // handshake in flight; READY will arrive via the bridge
    }

    if (_generation == generation && !_suppressAutoReconnect && !_disposed) {
      _setError('Reconnect failed after several attempts.');
    }
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
    _heldMouseButtons.clear();
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
  Future<void> waitForIdleForTest() =>
      _session?.waitForIdle() ?? Future.value();

  /// Test seam: exposes the auth-denied latch for reconnect-policy tests.
  @visibleForTesting
  bool authDeniedSinceLastConnect() => _authDeniedSinceLastConnect;

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
        // Engine-level signal (unexpected drop while previously connected):
        // the controller owns orchestration from here.
        _controller._onUnexpectedDrop();
        break;
      case ClientSessionState.connected:
      case ClientSessionState.connecting:
        break; // transient
      case ClientSessionState.closed:
      case ClientSessionState.disconnected:
        if (_controller._phase == AppConnectionPhase.connecting ||
            _controller._phase == AppConnectionPhase.pairing) {
          _controller._setPhase(AppConnectionPhase.error);
        }
        break;
    }
  }

  @override
  void onError(String message) {
    if (message.contains('Authentication denied')) {
      _controller._authDeniedSinceLastConnect = true;
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
