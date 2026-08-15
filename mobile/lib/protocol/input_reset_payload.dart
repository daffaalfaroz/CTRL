import 'dart:typed_data';

import 'frame.dart';
import 'payload_codec.dart';

const int inputResetReasonStateReset = 0x00;
const int inputResetReasonProfileSwitch = 0x01;
const int inputResetReasonMaintenance = 0x02;

class InputResetPayload {
  const InputResetPayload({required this.reason});

  final int reason;

  @override
  bool operator ==(Object other) =>
      other is InputResetPayload && reason == other.reason;

  @override
  int get hashCode => reason.hashCode;
}

class InputResetPayloadCodec {
  static Uint8List encode(InputResetPayload payload) {
    if (!_isKnownReason(payload.reason)) {
      throw const ProtocolException('Unknown INPUT_RESET reason.');
    }
    final writer = PayloadWriter()..writeUInt8(payload.reason);
    return writer.toBytes();
  }

  static InputResetPayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final reason = reader.readUInt8();
    reader.expectEnd();
    if (!_isKnownReason(reason)) {
      throw const ProtocolException('Unknown INPUT_RESET reason.');
    }
    return InputResetPayload(reason: reason);
  }

  static bool _isKnownReason(int reason) =>
      reason == inputResetReasonStateReset ||
      reason == inputResetReasonProfileSwitch ||
      reason == inputResetReasonMaintenance;
}
