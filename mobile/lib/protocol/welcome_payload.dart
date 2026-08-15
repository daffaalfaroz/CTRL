import 'dart:typed_data';

import 'frame.dart';
import 'payload_codec.dart';

class WelcomePayload {
  const WelcomePayload({
    required this.serverName,
    required this.effectiveMajor,
    required this.effectiveMinor,
    required this.minSupportedMajor,
    required this.sessionId,
    required this.authRequired,
    required this.challenge,
  });

  final String serverName;
  final int effectiveMajor;
  final int effectiveMinor;
  final int minSupportedMajor;
  final Uint8List sessionId;
  final bool authRequired;
  final Uint8List challenge;

  @override
  bool operator ==(Object other) =>
      other is WelcomePayload &&
      serverName == other.serverName &&
      effectiveMajor == other.effectiveMajor &&
      effectiveMinor == other.effectiveMinor &&
      minSupportedMajor == other.minSupportedMajor &&
      _bytesEqual(sessionId, other.sessionId) &&
      authRequired == other.authRequired &&
      _bytesEqual(challenge, other.challenge);

  @override
  int get hashCode => Object.hash(
        serverName,
        effectiveMajor,
        effectiveMinor,
        minSupportedMajor,
        Object.hashAll(sessionId),
        authRequired,
        Object.hashAll(challenge),
      );
}

class WelcomePayloadCodec {
  static Uint8List encode(WelcomePayload payload) {
    if (payload.sessionId.length != 16) {
      throw const ProtocolException('sessionId must be exactly 16 bytes.');
    }
    if (payload.challenge.length != 32) {
      throw const ProtocolException('challenge must be exactly 32 bytes.');
    }

    final writer = PayloadWriter()
      ..writeString(payload.serverName, minLength: 1, maxLength: 64)
      ..writeUInt8(payload.effectiveMajor)
      ..writeUInt8(payload.effectiveMinor)
      ..writeUInt8(payload.minSupportedMajor)
      ..writeBytes(payload.sessionId)
      ..writeUInt8(payload.authRequired ? 1 : 0)
      ..writeBytes(payload.challenge);
    return writer.toBytes();
  }

  static WelcomePayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final serverName = reader.readString(minLength: 1, maxLength: 64);
    final effectiveMajor = reader.readUInt8();
    final effectiveMinor = reader.readUInt8();
    final minSupportedMajor = reader.readUInt8();
    final sessionId = reader.readBytes(16);
    final authRequired = reader.readUInt8();
    final challenge = reader.readBytes(32);
    reader.expectEnd();

    if (authRequired > 1) {
      throw const ProtocolException('authRequired must be 0 or 1.');
    }

    return WelcomePayload(
      serverName: serverName,
      effectiveMajor: effectiveMajor,
      effectiveMinor: effectiveMinor,
      minSupportedMajor: minSupportedMajor,
      sessionId: sessionId,
      authRequired: authRequired == 1,
      challenge: challenge,
    );
  }
}

bool _bytesEqual(Uint8List a, Uint8List b) {
  if (a.length != b.length) return false;
  for (var i = 0; i < a.length; i++) {
    if (a[i] != b[i]) return false;
  }
  return true;
}
