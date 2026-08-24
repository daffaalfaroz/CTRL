import 'dart:convert';
import 'dart:typed_data';

import '../crypto/hmac.dart';
import '../protocol/auth_payload.dart';

/// Client-side authentication policy implementing docs/protocol.md §12 (M1.4.4):
///
///   challengeResponse = HMAC-SHA256(sharedSecret, challenge)
///
/// where sharedSecret is the raw credential — the pairing code for
/// credentialType 0x02, or the persisted token for 0x01 (never placed on the
/// wire for token auth: credentialLength stays 0).
class HmacAuthenticator {
  /// Pairing authentication: the code travels as the credential and doubles as
  /// the HMAC shared secret (§12 B).
  factory HmacAuthenticator.pairing({
    required String pairingCode,
    required String deviceId,
  }) =>
      HmacAuthenticator._(
        credentialType: authCredentialTypePairingCode,
        credential: pairingCode,
        deviceId: deviceId,
        secret: Uint8List.fromList(utf8.encode(pairingCode)),
      );

  /// Reconnect authentication with the persisted token: the secret signs the
  /// challenge but never appears in the payload (§12 A).
  factory HmacAuthenticator.token({
    required Uint8List token,
    required String deviceId,
  }) =>
      HmacAuthenticator._(
        credentialType: authCredentialTypeToken,
        credential: '',
        deviceId: deviceId,
        secret: token,
      );

  HmacAuthenticator._({
    required this.credentialType,
    required this.credential,
    required this.deviceId,
    required Uint8List secret,
  }) : secret = Uint8List.fromList(secret);

  final int credentialType;
  final String credential;
  final String deviceId;

  /// Raw shared secret (pairing code bytes or persisted token bytes).
  final Uint8List secret;

  /// HMAC-SHA256 over exactly 32 output bytes (§12 challengeResponse size).
  Uint8List challengeResponseFor(Uint8List challenge) =>
      hmacSha256(secret, challenge);

  AuthPayload buildAuth(Uint8List challenge) => AuthPayload(
        credentialType: credentialType,
        credential: credential,
        deviceId: deviceId,
        challengeResponse: challengeResponseFor(challenge),
      );
}
