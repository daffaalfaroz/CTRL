import 'dart:typed_data';

class ProtocolFrame {
  static const int magic = 0x4354;
  static const int headerSize = 18;
  static const int maxPayloadLength = 0xFFFF;

  const ProtocolFrame({
    required this.versionMajor,
    required this.versionMinor,
    required this.flags,
    required this.messageType,
    required this.sequence,
    required this.timestamp,
    required this.payload,
  });

  final int versionMajor;
  final int versionMinor;
  final int flags;
  final int messageType;
  final int sequence;
  final int timestamp;
  final Uint8List payload;

  @override
  bool operator ==(Object other) =>
      other is ProtocolFrame &&
      versionMajor == other.versionMajor &&
      versionMinor == other.versionMinor &&
      flags == other.flags &&
      messageType == other.messageType &&
      sequence == other.sequence &&
      timestamp == other.timestamp &&
      _bytesEqual(payload, other.payload);

  @override
  int get hashCode => Object.hash(
        versionMajor,
        versionMinor,
        flags,
        messageType,
        sequence,
        timestamp,
        Object.hashAll(payload),
      );
}

class ProtocolException implements Exception {
  const ProtocolException(this.message);
  final String message;

  @override
  String toString() => 'ProtocolException: $message';
}

class FrameCodec {
  static const int ackRequested = 0x01;
  static const int secure = 0x02;
  static const int compressed = 0x04;
  static const int mustUnderstand = 0x08;
  static const int _allowedFlags = ackRequested | mustUnderstand;

  static Uint8List encode(ProtocolFrame frame) {
    _validate(frame);

    final bytes = Uint8List(ProtocolFrame.headerSize + frame.payload.length);
    final data = ByteData.sublistView(bytes);
    data.setUint16(0, ProtocolFrame.magic, Endian.big);
    data.setUint8(2, frame.versionMajor);
    data.setUint8(3, frame.versionMinor);
    data.setUint8(4, frame.flags);
    data.setUint8(5, frame.messageType);
    data.setUint16(6, frame.payload.length, Endian.big);
    data.setUint16(8, frame.sequence, Endian.big);
    data.setUint64(10, frame.timestamp, Endian.big);
    bytes.setRange(ProtocolFrame.headerSize, bytes.length, frame.payload);
    return bytes;
  }

  static ProtocolFrame decode(Uint8List bytes) {
    if (bytes.length < ProtocolFrame.headerSize) {
      throw const ProtocolException('Frame header must be 18 bytes.');
    }

    final data = ByteData.sublistView(bytes);
    if (data.getUint16(0, Endian.big) != ProtocolFrame.magic) {
      throw const ProtocolException('Invalid frame magic.');
    }

    final flags = data.getUint8(4);
    if ((flags & ~_allowedFlags) != 0) {
      throw const ProtocolException(
        'Reserved frame flags must be zero in v1.',
      );
    }

    final payloadLength = data.getUint16(6, Endian.big);
    final expectedLength = ProtocolFrame.headerSize + payloadLength;
    if (bytes.length != expectedLength) {
      throw const ProtocolException(
        'Frame length does not match PayloadLength.',
      );
    }

    return ProtocolFrame(
      versionMajor: data.getUint8(2),
      versionMinor: data.getUint8(3),
      flags: flags,
      messageType: data.getUint8(5),
      sequence: data.getUint16(8, Endian.big),
      timestamp: data.getUint64(10, Endian.big),
      payload: Uint8List.fromList(
        bytes.sublist(ProtocolFrame.headerSize),
      ),
    );
  }

  static void _validate(ProtocolFrame frame) {
    if (frame.versionMajor < 0 || frame.versionMajor > 0xFF ||
        frame.versionMinor < 0 || frame.versionMinor > 0xFF ||
        frame.flags < 0 || frame.flags > 0xFF ||
        frame.messageType < 0 || frame.messageType > 0xFF ||
        frame.sequence < 0 || frame.sequence > 0xFFFF ||
        frame.payload.length > ProtocolFrame.maxPayloadLength) {
      throw const ProtocolException('Frame contains an out-of-range field.');
    }
    if ((frame.flags & ~_allowedFlags) != 0) {
      throw const ProtocolException(
        'Reserved frame flags must be zero in v1.',
      );
    }
  }
}

bool _bytesEqual(Uint8List a, Uint8List b) {
  if (a.length != b.length) return false;
  for (var i = 0; i < a.length; i++) {
    if (a[i] != b[i]) return false;
  }
  return true;
}
