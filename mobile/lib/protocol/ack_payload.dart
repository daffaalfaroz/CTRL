import 'dart:typed_data';

import 'payload_codec.dart';

class AckPayload {
  const AckPayload({required this.ackedSequence, required this.ackTime});

  final int ackedSequence;
  final int ackTime;

  @override
  bool operator ==(Object other) =>
      other is AckPayload &&
      ackedSequence == other.ackedSequence &&
      ackTime == other.ackTime;

  @override
  int get hashCode => Object.hash(ackedSequence, ackTime);
}

class AckPayloadCodec {
  static Uint8List encode(AckPayload payload) {
    final writer = PayloadWriter()
      ..writeUInt16(payload.ackedSequence)
      ..writeUInt64(payload.ackTime);
    return writer.toBytes();
  }

  static AckPayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final ackedSequence = reader.readUInt16();
    final ackTime = reader.readUInt64();
    reader.expectEnd();
    return AckPayload(ackedSequence: ackedSequence, ackTime: ackTime);
  }
}
