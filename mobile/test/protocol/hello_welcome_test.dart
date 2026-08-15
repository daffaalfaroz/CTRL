import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:ctrl_mobile/protocol/frame.dart';
import 'package:ctrl_mobile/protocol/hello_payload.dart';
import 'package:ctrl_mobile/protocol/payload_codec.dart';
import 'package:ctrl_mobile/protocol/welcome_payload.dart';

void main() {
  group('PayloadReader/PayloadWriter primitives', () {
    test('round-trips all primitive types', () {
      final writer = PayloadWriter()
        ..writeUInt8(0xAB)
        ..writeUInt16(0x1234)
        ..writeUInt32(0x01020304)
        ..writeUInt64(0x0102030405060708)
        ..writeFloat32(0.5)
        ..writeString('hi')
        ..writeString('€');

      final reader = PayloadReader(writer.toBytes());
      expect(reader.readUInt8(), 0xAB);
      expect(reader.readUInt16(), 0x1234);
      expect(reader.readUInt32(), 0x01020304);
      expect(reader.readUInt64(), 0x0102030405060708);
      expect(reader.readFloat32(), 0.5);
      expect(reader.readString(), 'hi');
      expect(reader.readString(), '€');
      expect(reader.remaining, 0);
      reader.expectEnd();
    });

    test('string prefix is UTF-8 byte length', () {
      final bytes = PayloadWriter()..writeString('€');
      expect(bytes.toBytes()[0], 3);
    });

    test('rejects truncated reads', () {
      expect(
        () => PayloadReader([0x01]).readUInt16(),
        throwsA(isA<ProtocolException>()),
      );
      expect(
        () => PayloadReader([0x00]).readUInt32(),
        throwsA(isA<ProtocolException>()),
      );
      expect(
        () => PayloadReader([0x00, 0x00, 0x00, 0x00]).readUInt64(),
        throwsA(isA<ProtocolException>()),
      );
      expect(
        () => PayloadReader([0x00]).readBytes(2),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects length prefix past end', () {
      expect(
        () => PayloadReader([0x02, 0x61]).readString(),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid UTF-8', () {
      expect(
        () => PayloadReader([0x01, 0xFF]).readString(),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes on expectEnd', () {
      final reader = PayloadReader([0x01, 0x41, 0x42]);
      expect(reader.readString(), 'A');
      expect(() => reader.expectEnd(), throwsA(isA<ProtocolException>()));
    });
  });

  group('HelloPayloadCodec', () {
    const hello = HelloPayload(
      deviceId: 'ctrl-42a8',
      clientVersion: '0.1.0',
      protocolMajor: 1,
      protocolMinor: 0,
      capabilities: 0x00000007,
    );

    test('encodes expected wire bytes from docs/protocol.md 19.1', () {
      const expected = <int>[
        0x09,
        0x63, 0x74, 0x72, 0x6C, 0x2D, 0x34, 0x32, 0x61, 0x38,
        0x05,
        0x30, 0x2E, 0x31, 0x2E, 0x30,
        0x01, 0x00,
        0x00, 0x00, 0x00, 0x07,
      ];
      expect(HelloPayloadCodec.encode(hello), expected);
    });

    test('round-trips', () {
      final decoded =
          HelloPayloadCodec.decode(HelloPayloadCodec.encode(hello));
      expect(decoded, hello);
    });

    test('rejects truncated payload', () {
      final encoded = HelloPayloadCodec.encode(hello);
      expect(
        () => HelloPayloadCodec.decode(
          encoded.sublist(0, encoded.length - 1),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      final encoded = HelloPayloadCodec.encode(hello);
      expect(
        () => HelloPayloadCodec.decode([...encoded, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid UTF-8 deviceId', () {
      expect(
        () => HelloPayloadCodec.decode([0x01, 0xFF, 0x00, 0x00, 0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects length prefix past end', () {
      expect(
        () => HelloPayloadCodec
            .decode([0x03, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('1-byte deviceId and clientVersion round-trip', () {
      const payload = HelloPayload(
        deviceId: 'a',
        clientVersion: 'b',
        protocolMajor: 1,
        protocolMinor: 0,
        capabilities: 0x00000007,
      );
      expect(HelloPayloadCodec.decode(HelloPayloadCodec.encode(payload)),
          payload);
    });

    test('64-byte deviceId and clientVersion round-trip', () {
      final payload = HelloPayload(
        deviceId: 'a' * 64,
        clientVersion: 'b' * 64,
        protocolMajor: 1,
        protocolMinor: 0,
        capabilities: 0x00000007,
      );
      expect(HelloPayloadCodec.decode(HelloPayloadCodec.encode(payload)),
          payload);
    });

    test('deviceId length uses UTF-8 byte count, not code points', () {
      const payload = HelloPayload(
        deviceId: 'é',
        clientVersion: 'c',
        protocolMajor: 1,
        protocolMinor: 0,
        capabilities: 0x00000007,
      );
      final bytes = HelloPayloadCodec.encode(payload);
      expect(bytes[0], 2);
    });

    test('rejects empty deviceId on encode', () {
      expect(
        () => HelloPayloadCodec.encode(
          const HelloPayload(
            deviceId: '',
            clientVersion: '0.1.0',
            protocolMajor: 1,
            protocolMinor: 0,
            capabilities: 0x00000007,
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects 65-byte deviceId on encode', () {
      expect(
        () => HelloPayloadCodec.encode(
          HelloPayload(
            deviceId: 'a' * 65,
            clientVersion: '0.1.0',
            protocolMajor: 1,
            protocolMinor: 0,
            capabilities: 0x00000007,
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects empty clientVersion on encode', () {
      expect(
        () => HelloPayloadCodec.encode(
          const HelloPayload(
            deviceId: 'ctrl-42a8',
            clientVersion: '',
            protocolMajor: 1,
            protocolMinor: 0,
            capabilities: 0x00000007,
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects 65-byte clientVersion on encode', () {
      expect(
        () => HelloPayloadCodec.encode(
          HelloPayload(
            deviceId: 'ctrl-42a8',
            clientVersion: 'c' * 65,
            protocolMajor: 1,
            protocolMinor: 0,
            capabilities: 0x00000007,
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects empty deviceId on decode', () {
      expect(
        () => HelloPayloadCodec.decode([0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects 65-byte deviceId length on decode', () {
      expect(
        () => HelloPayloadCodec.decode([0x41]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects empty clientVersion on decode', () {
      expect(
        () => HelloPayloadCodec.decode([
          0x09, 0x63, 0x74, 0x72, 0x6C, 0x2D, 0x34, 0x32, 0x61, 0x38, 0x00,
        ]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects 65-byte clientVersion length on decode', () {
      expect(
        () => HelloPayloadCodec.decode([
          0x09, 0x63, 0x74, 0x72, 0x6C, 0x2D, 0x34, 0x32, 0x61, 0x38, 0x41,
        ]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });

  group('WelcomePayloadCodec', () {
    final welcome = WelcomePayload(
      serverName: 'CTRL-PC',
      effectiveMajor: 1,
      effectiveMinor: 0,
      minSupportedMajor: 1,
      sessionId: Uint8List.fromList(List.generate(16, (i) => i)),
      authRequired: true,
      challenge: Uint8List.fromList(List.generate(32, (i) => 0x10 + i)),
    );

    test('encodes expected wire bytes from docs/protocol.md 19.2', () {
      final expected = <int>[
        0x07,
        0x43, 0x54, 0x52, 0x4C, 0x2D, 0x50, 0x43,
        0x01, 0x00, 0x01,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x01,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
        0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
        0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F,
      ];
      expect(WelcomePayloadCodec.encode(welcome), expected);
    });

    test('round-trips', () {
      final decoded =
          WelcomePayloadCodec.decode(WelcomePayloadCodec.encode(welcome));
      expect(decoded, welcome);
    });

    test('rejects truncated payload', () {
      final encoded = WelcomePayloadCodec.encode(welcome);
      expect(
        () => WelcomePayloadCodec.decode(
          encoded.sublist(0, encoded.length - 1),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      final encoded = WelcomePayloadCodec.encode(welcome);
      expect(
        () => WelcomePayloadCodec.decode([...encoded, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects short sessionId on encode', () {
      expect(
        () => WelcomePayloadCodec.encode(
          WelcomePayload(
            serverName: 'CTRL-PC',
            effectiveMajor: 1,
            effectiveMinor: 0,
            minSupportedMajor: 1,
            sessionId: Uint8List(15),
            authRequired: true,
            challenge: Uint8List(32),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects short challenge on encode', () {
      expect(
        () => WelcomePayloadCodec.encode(
          WelcomePayload(
            serverName: 'CTRL-PC',
            effectiveMajor: 1,
            effectiveMinor: 0,
            minSupportedMajor: 1,
            sessionId: Uint8List(16),
            authRequired: true,
            challenge: Uint8List(31),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects short sessionId on decode', () {
      final encoded = WelcomePayloadCodec.encode(welcome);
      final tampered = Uint8List.fromList([
        ...encoded.sublist(0, 26),
        ...encoded.sublist(27),
      ]);
      expect(
        () => WelcomePayloadCodec.decode(tampered),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid authRequired', () {
      final encoded = WelcomePayloadCodec.encode(welcome);
      encoded[27] = 0x02;
      expect(
        () => WelcomePayloadCodec.decode(encoded),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('1-byte serverName round-trips', () {
      final payload = WelcomePayload(
        serverName: 'a',
        effectiveMajor: 1,
        effectiveMinor: 0,
        minSupportedMajor: 1,
        sessionId: Uint8List(16),
        authRequired: true,
        challenge: Uint8List(32),
      );
      expect(WelcomePayloadCodec.decode(WelcomePayloadCodec.encode(payload)),
          payload);
    });

    test('64-byte serverName round-trips', () {
      final payload = WelcomePayload(
        serverName: 's' * 64,
        effectiveMajor: 1,
        effectiveMinor: 0,
        minSupportedMajor: 1,
        sessionId: Uint8List(16),
        authRequired: true,
        challenge: Uint8List(32),
      );
      expect(WelcomePayloadCodec.decode(WelcomePayloadCodec.encode(payload)),
          payload);
    });

    test('serverName length uses UTF-8 byte count, not code points', () {
      final payload = WelcomePayload(
        serverName: 'é',
        effectiveMajor: 1,
        effectiveMinor: 0,
        minSupportedMajor: 1,
        sessionId: Uint8List(16),
        authRequired: true,
        challenge: Uint8List(32),
      );
      final bytes = WelcomePayloadCodec.encode(payload);
      expect(bytes[0], 2);
    });

    test('rejects empty serverName on encode', () {
      expect(
        () => WelcomePayloadCodec.encode(
          WelcomePayload(
            serverName: '',
            effectiveMajor: 1,
            effectiveMinor: 0,
            minSupportedMajor: 1,
            sessionId: Uint8List(16),
            authRequired: true,
            challenge: Uint8List(32),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects 65-byte serverName on encode', () {
      expect(
        () => WelcomePayloadCodec.encode(
          WelcomePayload(
            serverName: 's' * 65,
            effectiveMajor: 1,
            effectiveMinor: 0,
            minSupportedMajor: 1,
            sessionId: Uint8List(16),
            authRequired: true,
            challenge: Uint8List(32),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects empty serverName on decode', () {
      expect(
        () => WelcomePayloadCodec.decode([0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects 65-byte serverName length on decode', () {
      expect(
        () => WelcomePayloadCodec.decode([0x41]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });
}
