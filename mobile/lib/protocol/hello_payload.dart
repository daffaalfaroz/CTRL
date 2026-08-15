import 'dart:typed_data';

import 'payload_codec.dart';

class HelloPayload {
  const HelloPayload({
    required this.deviceId,
    required this.clientVersion,
    required this.protocolMajor,
    required this.protocolMinor,
    required this.capabilities,
  });

  final String deviceId;
  final String clientVersion;
  final int protocolMajor;
  final int protocolMinor;
  final int capabilities;

  @override
  bool operator ==(Object other) =>
      other is HelloPayload &&
      deviceId == other.deviceId &&
      clientVersion == other.clientVersion &&
      protocolMajor == other.protocolMajor &&
      protocolMinor == other.protocolMinor &&
      capabilities == other.capabilities;

  @override
  int get hashCode => Object.hash(
        deviceId,
        clientVersion,
        protocolMajor,
        protocolMinor,
        capabilities,
      );
}

class HelloPayloadCodec {
  static Uint8List encode(HelloPayload payload) {
    final writer = PayloadWriter()
      ..writeString(payload.deviceId, minLength: 1, maxLength: 64)
      ..writeString(payload.clientVersion, minLength: 1, maxLength: 64)
      ..writeUInt8(payload.protocolMajor)
      ..writeUInt8(payload.protocolMinor)
      ..writeUInt32(payload.capabilities);
    return writer.toBytes();
  }

  static HelloPayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final result = HelloPayload(
      deviceId: reader.readString(minLength: 1, maxLength: 64),
      clientVersion: reader.readString(minLength: 1, maxLength: 64),
      protocolMajor: reader.readUInt8(),
      protocolMinor: reader.readUInt8(),
      capabilities: reader.readUInt32(),
    );
    reader.expectEnd();
    return result;
  }
}
