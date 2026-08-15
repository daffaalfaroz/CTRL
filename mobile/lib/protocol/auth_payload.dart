import 'dart:typed_data';

import 'frame.dart';
import 'payload_codec.dart';

const int authCredentialTypeToken = 0x01;
const int authCredentialTypePairingCode = 0x02;

class AuthPayload {
  const AuthPayload({
    required this.credentialType,
    required this.credential,
    required this.deviceId,
    required this.challengeResponse,
  });

  final int credentialType;
  final String credential;
  final String deviceId;
  final Uint8List challengeResponse;

  @override
  bool operator ==(Object other) =>
      other is AuthPayload &&
      credentialType == other.credentialType &&
      credential == other.credential &&
      deviceId == other.deviceId &&
      _bytesEqual(challengeResponse, other.challengeResponse);

  @override
  int get hashCode => Object.hash(
        credentialType,
        credential,
        deviceId,
        Object.hashAll(challengeResponse),
      );
}

class AuthPayloadCodec {
  static Uint8List encode(AuthPayload payload) {
    if (!_isKnownCredentialType(payload.credentialType)) {
      throw const ProtocolException('Unknown credentialType.');
    }
    if (payload.credentialType == authCredentialTypeToken &&
        payload.credential.isNotEmpty) {
      throw const ProtocolException('Token credential must be empty.');
    }
    if (payload.challengeResponse.length != 32) {
      throw const ProtocolException(
          'challengeResponse must be exactly 32 bytes.');
    }

    final writer = PayloadWriter()
      ..writeUInt8(payload.credentialType)
      ..writeString(payload.credential)
      ..writeString(payload.deviceId, minLength: 1, maxLength: 64)
      ..writeBytes(payload.challengeResponse);
    return writer.toBytes();
  }

  static AuthPayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final credentialType = reader.readUInt8();
    final credential = reader.readString();
    final deviceId = reader.readString(minLength: 1, maxLength: 64);
    final challengeResponse = reader.readBytes(32);
    reader.expectEnd();

    if (!_isKnownCredentialType(credentialType)) {
      throw const ProtocolException('Unknown credentialType.');
    }
    if (credentialType == authCredentialTypeToken && credential.isNotEmpty) {
      throw const ProtocolException('Token credential must have length 0.');
    }

    return AuthPayload(
      credentialType: credentialType,
      credential: credential,
      deviceId: deviceId,
      challengeResponse: challengeResponse,
    );
  }

  static bool _isKnownCredentialType(int credentialType) =>
      credentialType == authCredentialTypeToken ||
      credentialType == authCredentialTypePairingCode;
}

bool _bytesEqual(Uint8List a, Uint8List b) {
  if (a.length != b.length) return false;
  for (var i = 0; i < a.length; i++) {
    if (a[i] != b[i]) return false;
  }
  return true;
}
