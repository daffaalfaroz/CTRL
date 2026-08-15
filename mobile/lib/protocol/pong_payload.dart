import 'dart:typed_data';

import 'payload_codec.dart';

class PongPayload {
  const PongPayload({required this.clientSendTime, required this.serverTime});

  final int clientSendTime;
  final int serverTime;

  @override
  bool operator ==(Object other) =>
      other is PongPayload &&
      clientSendTime == other.clientSendTime &&
      serverTime == other.serverTime;

  @override
  int get hashCode => Object.hash(clientSendTime, serverTime);
}

class PongPayloadCodec {
  static Uint8List encode(PongPayload payload) {
    final writer = PayloadWriter()
      ..writeUInt64(payload.clientSendTime)
      ..writeUInt64(payload.serverTime);
    return writer.toBytes();
  }

  static PongPayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final clientSendTime = reader.readUInt64();
    final serverTime = reader.readUInt64();
    reader.expectEnd();
    return PongPayload(clientSendTime: clientSendTime, serverTime: serverTime);
  }
}
