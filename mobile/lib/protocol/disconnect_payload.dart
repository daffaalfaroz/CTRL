import 'dart:typed_data';

import 'frame.dart';
import 'payload_codec.dart';

const int disconnectReasonNormal = 0x00;
const int disconnectReasonAppClosing = 0x01;
const int disconnectReasonServerRestart = 0x02;
const int disconnectReasonIdleTimeout = 0x03;
const int disconnectReasonSecurity = 0x04;
const int disconnectReasonProtocolViolation = 0x05;

class DisconnectPayload {
  const DisconnectPayload({required this.reason});

  final int reason;

  @override
  bool operator ==(Object other) =>
      other is DisconnectPayload && reason == other.reason;

  @override
  int get hashCode => reason.hashCode;
}

class DisconnectPayloadCodec {
  static Uint8List encode(DisconnectPayload payload) {
    if (!_isKnownReason(payload.reason)) {
      throw const ProtocolException('Unknown DISCONNECT reason.');
    }

    final writer = PayloadWriter()..writeUInt8(payload.reason);
    return writer.toBytes();
  }

  static DisconnectPayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final reason = reader.readUInt8();
    reader.expectEnd();

    if (!_isKnownReason(reason)) {
      throw const ProtocolException('Unknown DISCONNECT reason.');
    }

    return DisconnectPayload(reason: reason);
  }

  static bool _isKnownReason(int reason) => reason <= disconnectReasonProtocolViolation;
}
