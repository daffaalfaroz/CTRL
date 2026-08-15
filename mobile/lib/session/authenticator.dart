import 'dart:typed_data';

import '../protocol/auth_payload.dart';

/// Client-side authentication policy. D4 keeps a stub: challengeResponse is a
/// plain echo of the server challenge (no crypto). The next milestone replaces
/// this with HMAC-SHA256(token|pairingCode, challenge).
class EchoAuthenticator {
  const EchoAuthenticator({
    required this.credentialType,
    required this.credential,
    required this.deviceId,
  });

  final int credentialType;
  final String credential;
  final String deviceId;

  /// D4 stub: echo the challenge back (no HMAC yet).
  Uint8List challengeResponseFor(Uint8List challenge) =>
      Uint8List.fromList(challenge);

  AuthPayload buildAuth(Uint8List challenge) => AuthPayload(
        credentialType: credentialType,
        credential: credential,
        deviceId: deviceId,
        challengeResponse: challengeResponseFor(challenge),
      );
}