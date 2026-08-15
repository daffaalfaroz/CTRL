import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:ctrl_mobile/protocol/frame.dart';

void main() {
  test('encodes and decodes the 18-byte big-endian header', () {
    final frame = ProtocolFrame(
      versionMajor: 1,
      versionMinor: 0,
      flags: FrameCodec.ackRequested,
      messageType: 0x01,
      sequence: 0x1234,
      timestamp: 0x0102030405060708,
      payload: Uint8List.fromList([0xDE, 0xAD, 0xBE, 0xEF]),
    );

    final encoded = FrameCodec.encode(frame);

    expect(encoded.length, ProtocolFrame.headerSize + 4);
    expect(encoded.sublist(0, 2), [0x43, 0x54]);
    expect(encoded.sublist(6, 8), [0x00, 0x04]);
    expect(encoded.sublist(8, 10), [0x12, 0x34]);
    expect(encoded.sublist(10, 18), [
      0x01,
      0x02,
      0x03,
      0x04,
      0x05,
      0x06,
      0x07,
      0x08,
    ]);
    expect(FrameCodec.decode(encoded), frame);
  });

  test('rejects truncated frames', () {
    expect(
      () => FrameCodec.decode(Uint8List(ProtocolFrame.headerSize - 1)),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('rejects invalid magic', () {
    final bytes = Uint8List(ProtocolFrame.headerSize);
    bytes[6] = 0;
    bytes[7] = 0;
    expect(
      () => FrameCodec.decode(bytes),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('rejects reserved flags', () {
    final bytes = Uint8List(ProtocolFrame.headerSize);
    bytes[0] = 0x43;
    bytes[1] = 0x54;
    bytes[4] = FrameCodec.secure;
    expect(
      () => FrameCodec.decode(bytes),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('rejects payload length mismatch', () {
    final frame = ProtocolFrame(
      versionMajor: 1,
      versionMinor: 0,
      flags: 0,
      messageType: 0x01,
      sequence: 0,
      timestamp: 1,
      payload: Uint8List.fromList([1, 2, 3]),
    );
    final encoded = FrameCodec.encode(frame);
    encoded[7] = 4;

    expect(
      () => FrameCodec.decode(encoded),
      throwsA(isA<ProtocolException>()),
    );
  });
}
