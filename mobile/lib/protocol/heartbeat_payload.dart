import 'dart:typed_data';

import 'payload_codec.dart';

class HeartbeatPayload {
  const HeartbeatPayload({required this.clientSendTime});

  final int clientSendTime;

  @override
  bool operator ==(Object other) =>
      other is HeartbeatPayload && clientSendTime == other.clientSendTime;

  @override
  int get hashCode => clientSendTime.hashCode;
}

class HeartbeatPayloadCodec {
  static Uint8List encode(HeartbeatPayload payload) {
    final writer = PayloadWriter()..writeUInt64(payload.clientSendTime);
    return writer.toBytes();
  }

  static HeartbeatPayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final clientSendTime = reader.readUInt64();
    reader.expectEnd();
    return HeartbeatPayload(clientSendTime: clientSendTime);
  }
}
