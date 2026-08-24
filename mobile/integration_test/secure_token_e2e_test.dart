import 'package:flutter_test/flutter_test.dart';
import 'package:ctrl_mobile/protocol/input_event.dart';
import 'package:ctrl_mobile/protocol/input_snapshot_payload.dart';
import 'package:ctrl_mobile/session/authenticator.dart';
import 'package:ctrl_mobile/session/client_session.dart';
import 'package:ctrl_mobile/session/input_snapshot_provider.dart';
import 'package:ctrl_mobile/session/session_listener.dart';
import 'package:ctrl_mobile/session/session_state.dart';
import 'package:ctrl_mobile/session/secure_token_store.dart';
import 'package:ctrl_mobile/transport/tcp_transport.dart';
import 'package:integration_test/integration_test.dart';

/// M1.4.5 on-device end-to-end verification of secure token persistence.
///
/// Run twice against the C# integration server (with `adb reverse` in place):
///
/// ```
/// flutter test integration_test/secure_token_e2e_test.dart \
///   -d device --dart-define=TOKEN_PHASE=write --dart-define=SERVER_PORT=p
/// flutter test integration_test/secure_token_e2e_test.dart \
///   -d device --dart-define=TOKEN_PHASE=read  --dart-define=SERVER_PORT=p
/// ```
///
/// Each invocation is a SEPARATE Android process, so the `read` phase proves
/// the newToken issued during pairing survived a full app restart through
/// Keystore-backed storage, and still authenticates (HMAC token reconnect).
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  const phase = String.fromEnvironment('TOKEN_PHASE');
  const port = int.fromEnvironment('SERVER_PORT');
  const deviceId = 'e2e-device';
  const pairingCode = '123456'; // pinned by the C# integration server

  test('secure token survives a full process restart and re-authenticates',
      () async {
    expect(phase, anyOf('write', 'read'),
        reason: 'run with --dart-define=TOKEN_PHASE=write|read');
    final store = SecureTokenStore();

    if (phase == 'write') {
      await store.delete(deviceId); // start from a clean slate
      final transport = TcpTransport(port: port);
      await transport.connect();
      final session = _makeSession(transport, store,
          HmacAuthenticator.pairing(pairingCode: pairingCode, deviceId: deviceId));
      session.connect();
      await _waitUntilReady(session);

      final stored = await store.load(deviceId);
      expect(stored, isNotNull,
          reason: 'pairing must persist newToken via SecureTokenStore');
      expect(stored!.length, 32);
      await transport.close();
    } else {
      // New process: read what the previous one persisted.
      final restored = await store.load(deviceId);
      expect(restored, isNotNull,
          reason:
              'token must survive a full app restart via Android Keystore-backed '
              'storage (no plaintext fallback exists by design)');
      expect(restored!.length, 32);

      final transport = TcpTransport(port: port);
      await transport.connect();
      final session = _makeSession(
          transport, store, HmacAuthenticator.token(token: restored, deviceId: deviceId));
      session.connect();
      await _waitUntilReady(session);
      await transport.close();
    }
  });
}

ClientSession _makeSession(TcpTransport transport, SecureTokenStore store,
    HmacAuthenticator authenticator) {
  return ClientSession(
    transport: transport,
    authenticator: authenticator,
    listener: _NoopListener(),
    inputSnapshotProvider: const _SingleButtonSnapshot(),
    tokenStore: store,
  );
}

Future<void> _waitUntilReady(ClientSession session) async {
  final deadline =
      DateTime.now().add(const Duration(seconds: 15));
  while (session.state != ClientSessionState.ready) {
    if (session.state == ClientSessionState.closed ||
        session.state == ClientSessionState.disconnected) {
      fail('session ended in state ${session.state} before becoming ready');
    }
    if (DateTime.now().isAfter(deadline)) {
      fail('session never became ready (state: ${session.state})');
    }
    await Future<void>.delayed(const Duration(milliseconds: 50));
  }
}

class _NoopListener implements SessionListener {
  @override
  void onError(String message) {}

  @override
  void onFatalError(error) {}

  @override
  void onPong(pong) {}

  @override
  void onStateChanged(state) {}
}

class _SingleButtonSnapshot implements InputSnapshotProvider {
  const _SingleButtonSnapshot();

  @override
  InputSnapshotPayload currentSnapshot() => InputSnapshotPayload(events: [
        const InputEvent(
          controlId: 'e2e',
          kind: inputEventKindButton,
          flags: inputEventFlagStateChanged | inputEventFlagInitial,
          state: inputEventStateDown,
          pressCount: 1,
        ),
      ]);
}
