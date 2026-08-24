import 'dart:async';
import 'dart:io';

import 'package:flutter/widgets.dart';
import 'package:ctrl_mobile/protocol/input_event.dart';
import 'package:ctrl_mobile/protocol/input_snapshot_payload.dart';
import 'package:ctrl_mobile/session/authenticator.dart';
import 'package:ctrl_mobile/session/client_session.dart';
import 'package:ctrl_mobile/session/input_snapshot_provider.dart';
import 'package:ctrl_mobile/session/session_listener.dart';
import 'package:ctrl_mobile/session/session_state.dart';
import 'package:ctrl_mobile/session/secure_token_store.dart';
import 'package:ctrl_mobile/transport/tcp_transport.dart';

/// Manual on-device verifier for M1.4.5 secure token persistence.
///
/// Unlike `flutter test integration_test` (which uninstalls the app after every
/// run and therefore wipes data + Keystore material), this entry point is run
/// via `flutter run` twice; the app stays installed between runs, so the READ
/// phase is a genuine new-process restart over Android Keystore-backed storage.
///
/// ```
/// flutter run -d device -t lib/token_e2e_main.dart \
///   --dart-define=TOKEN_PHASE=write --dart-define=SERVER_PORT=p
/// adb shell am force-stop com.ctrl.ctrl_mobile
/// flutter run -d device -t lib/token_e2e_main.dart \
///   --dart-define=TOKEN_PHASE=read --dart-define=SERVER_PORT=p
/// ```
///
/// Prints E2E:WRITE:OK / E2E:READ:OK and exits 0 on success; exits 1 on any
/// failure. Never logs token bytes.
Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  const phase = String.fromEnvironment('TOKEN_PHASE');
  const port = int.fromEnvironment('SERVER_PORT');
  const deviceId = 'e2e-device';
  const pairingCode = '123456'; // pinned by the C# integration server

  Future<void> fail(String why) => _finish(
      1, 'E2E:${phase.toUpperCase()}:FAIL:$why');

  try {
    final store = SecureTokenStore();

    if (phase == 'write') {
      await store.delete(deviceId);
      final transport = TcpTransport(port: port);
      await transport.connect();
      final session = _session(transport, store,
          HmacAuthenticator.pairing(pairingCode: pairingCode, deviceId: deviceId));
      session.connect();
      await _waitReady(session);
      final stored = await store.load(deviceId);
      if (stored == null || stored.length != 32) {
        await fail('newToken missing after pairing');
        return;
      }
      await _finish(0, 'E2E:WRITE:OK');
    } else if (phase == 'read') {
      final restored = await store.load(deviceId);
      if (restored == null || restored.length != 32) {
        await fail('token did not survive the restart through secure storage');
        return;
      }
      final transport = TcpTransport(port: port);
      await transport.connect();
      final session = _session(transport, store,
          HmacAuthenticator.token(token: restored, deviceId: deviceId));
      session.connect();
      await _waitReady(session);
      await _finish(0, 'E2E:READ:OK');
    } else {
      await fail('unknown TOKEN_PHASE "$phase"');
    }
  } catch (e) {
    await fail('$e');
  }
}

/// Prints the marker AND writes it to a file readable via `adb shell run-as`,
/// because stdout under `flutter run` is tunneled through the VM service and
/// is lost when the process exits.
Future<void> _finish(int code, String marker) async {
  stdout.writeln(marker);
  try {
    final result = File(
        '/data/data/com.ctrl.ctrl_mobile/files/e2e_result.txt');
    await result.writeAsString('$marker\n', flush: true);
  } catch (_) {
    // Fall back to the stdout marker alone.
  }
  await Future<void>.delayed(const Duration(milliseconds: 500));
  exit(code);
}

ClientSession _session(TcpTransport transport, SecureTokenStore store,
    HmacAuthenticator authenticator) {
  return ClientSession(
    transport: transport,
    authenticator: authenticator,
    listener: _Listener(),
    inputSnapshotProvider: const _Snapshot(),
    tokenStore: store,
  );
}

Future<void> _waitReady(ClientSession session) async {
  final deadline = DateTime.now().add(const Duration(seconds: 15));
  while (session.state != ClientSessionState.ready) {
    if (session.state == ClientSessionState.closed ||
        session.state == ClientSessionState.disconnected) {
      throw StateError('session ended in ${session.state}');
    }
    if (DateTime.now().isAfter(deadline)) {
      throw TimeoutException('not ready', const Duration(seconds: 15));
    }
    await Future<void>.delayed(const Duration(milliseconds: 50));
  }
}

class _Listener implements SessionListener {
  @override
  void onError(String message) => stdout.writeln('E2E:onError:$message');

  @override
  void onFatalError(error) {}

  @override
  void onPong(pong) {}

  @override
  void onStateChanged(state) {}
}

class _Snapshot implements InputSnapshotProvider {
  const _Snapshot();

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
