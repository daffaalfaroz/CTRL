import 'dart:typed_data';

import 'frame.dart';
import 'payload_codec.dart';

const int authOkResultOk = 0x00;

class AuthOkPayload {
  const AuthOkPayload({
    required this.result,
    required this.sessionId,
    required this.serverCapabilities,
    required this.newToken,
  });

  final int result;
  final Uint8List sessionId;
  final int serverCapabilities;
  final String newToken;

  @override
  bool operator ==(Object other) =>
      other is AuthOkPayload &&
      result == other.result &&
      _bytesEqual(sessionId, other.sessionId) &&
      serverCapabilities == other.serverCapabilities &&
      newToken == other.newToken;

  @override
  int get hashCode => Object.hash(
        result,
        Object.hashAll(sessionId),
        serverCapabilities,
        newToken,
      );
}

class AuthOkPayloadCodec {
  static Uint8List encode(AuthOkPayload payload) {
    if (payload.result != authOkResultOk) {
      throw const ProtocolException('Unknown AUTH_OK result.');
    }
    if (payload.sessionId.length != 16) {
      throw const ProtocolException('sessionId must be exactly 16 bytes.');
    }

    final writer = PayloadWriter()
      ..writeUInt8(payload.result)
      ..writeBytes(payload.sessionId)
      ..writeUInt32(payload.serverCapabilities)
      ..writeString(payload.newToken);
    return writer.toBytes();
  }

  static AuthOkPayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final result = reader.readUInt8();
    final sessionId = reader.readBytes(16);
    final serverCapabilities = reader.readUInt32();
    final newToken = reader.readString();
    reader.expectEnd();

    if (result != authOkResultOk) {
      throw const ProtocolException('Unknown AUTH_OK result.');
    }

    return AuthOkPayload(
      result: result,
      sessionId: sessionId,
      serverCapabilities: serverCapabilities,
      newToken: newToken,
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
