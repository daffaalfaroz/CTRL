import 'dart:typed_data';

import 'frame.dart';
import 'payload_codec.dart';

const int authDeniedReasonBadCredential = 0x01;
const int authDeniedReasonExpiredCode = 0x02;
const int authDeniedReasonDeviceLimit = 0x03;

class AuthDeniedPayload {
  const AuthDeniedPayload({
    required this.reason,
    required this.message,
  });

  final int reason;
  final String message;

  @override
  bool operator ==(Object other) =>
      other is AuthDeniedPayload &&
      reason == other.reason &&
      message == other.message;

  @override
  int get hashCode => Object.hash(reason, message);
}

class AuthDeniedPayloadCodec {
  static Uint8List encode(AuthDeniedPayload payload) {
    if (!_isKnownReason(payload.reason)) {
      throw const ProtocolException('Unknown AUTH_DENIED reason.');
    }

    final writer = PayloadWriter()
      ..writeUInt8(payload.reason)
      ..writeString(payload.message);
    return writer.toBytes();
  }

  static AuthDeniedPayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final reason = reader.readUInt8();
    final message = reader.readString();
    reader.expectEnd();

    if (!_isKnownReason(reason)) {
      throw const ProtocolException('Unknown AUTH_DENIED reason.');
    }

    return AuthDeniedPayload(reason: reason, message: message);
  }

  static bool _isKnownReason(int reason) =>
      reason == authDeniedReasonBadCredential ||
      reason == authDeniedReasonExpiredCode ||
      reason == authDeniedReasonDeviceLimit;
}
