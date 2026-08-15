import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:ctrl_mobile/protocol/auth_denied_payload.dart';
import 'package:ctrl_mobile/protocol/auth_ok_payload.dart';
import 'package:ctrl_mobile/protocol/auth_payload.dart';
import 'package:ctrl_mobile/protocol/frame.dart';

void main() {
  group('AuthPayloadCodec', () {
    final pairingAuth = AuthPayload(
      credentialType: authCredentialTypePairingCode,
      credential: '123456',
      deviceId: 'ctrl-42a8',
      challengeResponse: Uint8List.fromList(List.generate(32, (i) => 0x50 + i)),
    );

    test('encodes expected wire bytes from docs/protocol.md 19.3', () {
      const expected = <int>[
        0x02, 0x06,
        0x31, 0x32, 0x33, 0x34, 0x35, 0x36,
        0x09,
        0x63, 0x74, 0x72, 0x6C, 0x2D, 0x34, 0x32, 0x61, 0x38,
        0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
        0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
        0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F,
      ];
      expect(AuthPayloadCodec.encode(pairingAuth), expected);
    });

    test('round-trips pairing code auth', () {
      final decoded =
          AuthPayloadCodec.decode(AuthPayloadCodec.encode(pairingAuth));
      expect(decoded, pairingAuth);
    });

    test('token auth encodes empty credential (credentialLength=0)', () {
      final token = AuthPayload(
        credentialType: authCredentialTypeToken,
        credential: '',
        deviceId: 'ctrl-42a8',
        challengeResponse: Uint8List(32),
      );
      final bytes = AuthPayloadCodec.encode(token);
      expect(bytes.sublist(0, 2), [0x01, 0x00]);
      final decoded = AuthPayloadCodec.decode(bytes);
      expect(decoded, token);
    });

    test('credential at 255-byte boundary round-trips', () {
      final boundary = AuthPayload(
        credentialType: authCredentialTypePairingCode,
        credential: 'a' * 255,
        deviceId: 'd',
        challengeResponse: Uint8List(32),
      );
      expect(
        AuthPayloadCodec.decode(AuthPayloadCodec.encode(boundary)),
        boundary,
      );
    });

    test('rejects unknown credentialType on decode', () {
      final encoded = AuthPayloadCodec.encode(pairingAuth);
      encoded[0] = 0x03;
      expect(
        () => AuthPayloadCodec.decode(encoded),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects token auth with non-empty credential on decode', () {
      final bytes = <int>[
        0x01, 0x01, 0x41, 0x00,
        ...List.filled(32, 0),
      ];
      expect(
        () => AuthPayloadCodec.decode(bytes),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects token auth with non-empty credential on encode', () {
      expect(
        () => AuthPayloadCodec.encode(
          AuthPayload(
            credentialType: authCredentialTypeToken,
            credential: 'x',
            deviceId: 'd',
            challengeResponse: Uint8List(32),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects unknown credentialType on encode', () {
      expect(
        () => AuthPayloadCodec.encode(
          AuthPayload(
            credentialType: 0x03,
            credential: '',
            deviceId: 'd',
            challengeResponse: Uint8List(32),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects challengeResponse that is not 32 bytes on encode', () {
      expect(
        () => AuthPayloadCodec.encode(
          AuthPayload(
            credentialType: authCredentialTypePairingCode,
            credential: '',
            deviceId: 'd',
            challengeResponse: Uint8List(31),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects credential exceeding 255 bytes on encode', () {
      expect(
        () => AuthPayloadCodec.encode(
          AuthPayload(
            credentialType: authCredentialTypePairingCode,
            credential: 'a' * 256,
            deviceId: 'd',
            challengeResponse: Uint8List(32),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects truncated payload', () {
      final encoded = AuthPayloadCodec.encode(pairingAuth);
      expect(
        () => AuthPayloadCodec.decode(encoded.sublist(0, encoded.length - 1)),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      final encoded = AuthPayloadCodec.encode(pairingAuth);
      expect(
        () => AuthPayloadCodec.decode([...encoded, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid UTF-8 deviceId', () {
      final bytes = <int>[
        0x02, 0x00, 0x01, 0xFF,
        ...List.filled(32, 0),
      ];
      expect(
        () => AuthPayloadCodec.decode(bytes),
        throwsA(isA<ProtocolException>()),
      );
    });
  });

  group('AuthOkPayloadCodec', () {
    final ok = AuthOkPayload(
      result: authOkResultOk,
      sessionId: Uint8List.fromList(List.generate(16, (i) => i)),
      serverCapabilities: 0x00000007,
      newToken: 'a1b2c3d4e5f60718',
    );

    test('encodes expected wire bytes from docs/protocol.md 19.4', () {
      const expected = <int>[
        0x00,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x00, 0x00, 0x00, 0x07,
        0x10,
        0x61, 0x31, 0x62, 0x32, 0x63, 0x33, 0x64, 0x34, 0x65, 0x35,
        0x66, 0x36, 0x30, 0x37, 0x31, 0x38,
      ];
      expect(AuthOkPayloadCodec.encode(ok), expected);
    });

    test('round-trips with newToken', () {
      final decoded = AuthOkPayloadCodec.decode(AuthOkPayloadCodec.encode(ok));
      expect(decoded, ok);
    });

    test('round-trips with empty newToken', () {
      final noToken = AuthOkPayload(
        result: authOkResultOk,
        sessionId: Uint8List(16),
        serverCapabilities: 0x00000007,
        newToken: '',
      );
      final bytes = AuthOkPayloadCodec.encode(noToken);
      expect(bytes[bytes.length - 1], 0x00);
      expect(AuthOkPayloadCodec.decode(bytes), noToken);
    });

    test('rejects sessionId that is not 16 bytes on encode', () {
      expect(
        () => AuthOkPayloadCodec.encode(
          AuthOkPayload(
            result: authOkResultOk,
            sessionId: Uint8List(15),
            serverCapabilities: 0x00000007,
            newToken: '',
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects short sessionId on decode', () {
      final encoded = AuthOkPayloadCodec.encode(ok);
      final tampered = Uint8List.fromList([
        ...encoded.sublist(0, 10),
        ...encoded.sublist(11),
      ]);
      expect(
        () => AuthOkPayloadCodec.decode(tampered),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects unknown result on decode', () {
      final encoded = AuthOkPayloadCodec.encode(ok);
      encoded[0] = 0x01;
      expect(
        () => AuthOkPayloadCodec.decode(encoded),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects unknown result on encode', () {
      expect(
        () => AuthOkPayloadCodec.encode(
          AuthOkPayload(
            result: 0x01,
            sessionId: Uint8List(16),
            serverCapabilities: 0x00000007,
            newToken: '',
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects truncated payload', () {
      final encoded = AuthOkPayloadCodec.encode(ok);
      expect(
        () => AuthOkPayloadCodec.decode(encoded.sublist(0, encoded.length - 1)),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      final encoded = AuthOkPayloadCodec.encode(ok);
      expect(
        () => AuthOkPayloadCodec.decode([...encoded, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });

  group('AuthDeniedPayloadCodec', () {
    const denied = AuthDeniedPayload(
      reason: authDeniedReasonBadCredential,
      message: 'auth failed',
    );

    test('encodes expected wire bytes from docs/protocol.md 19.13', () {
      const expected = <int>[
        0x01, 0x0B,
        0x61, 0x75, 0x74, 0x68, 0x20, 0x66, 0x61, 0x69, 0x6C, 0x65, 0x64,
      ];
      expect(AuthDeniedPayloadCodec.encode(denied), expected);
    });

    test('round-trips all defined reasons', () {
      for (final reason in [
        authDeniedReasonBadCredential,
        authDeniedReasonExpiredCode,
        authDeniedReasonDeviceLimit,
      ]) {
        final payload = AuthDeniedPayload(reason: reason, message: 'x');
        expect(
          AuthDeniedPayloadCodec.decode(AuthDeniedPayloadCodec.encode(payload)),
          payload,
        );
      }
    });

    test('round-trips empty message', () {
      const payload = AuthDeniedPayload(
        reason: authDeniedReasonExpiredCode,
        message: '',
      );
      final bytes = AuthDeniedPayloadCodec.encode(payload);
      expect(bytes, [0x02, 0x00]);
      expect(AuthDeniedPayloadCodec.decode(bytes), payload);
    });

    test('message length is UTF-8 byte length', () {
      final bytes = AuthDeniedPayloadCodec.encode(
        const AuthDeniedPayload(
          reason: authDeniedReasonBadCredential,
          message: 'échec',
        ),
      );
      expect(bytes[1], 6);
    });

    test('rejects unknown reason on decode', () {
      for (final reason in [0x00, 0x04]) {
        expect(
          () => AuthDeniedPayloadCodec.decode([reason, 0x00]),
          throwsA(isA<ProtocolException>()),
        );
      }
    });

    test('rejects unknown reason on encode', () {
      expect(
        () => AuthDeniedPayloadCodec.encode(
          const AuthDeniedPayload(reason: 0x04, message: 'x'),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid UTF-8 message', () {
      expect(
        () => AuthDeniedPayloadCodec.decode([0x01, 0x01, 0xFF]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects truncated payload', () {
      final encoded = AuthDeniedPayloadCodec.encode(denied);
      expect(
        () => AuthDeniedPayloadCodec.decode(
          encoded.sublist(0, encoded.length - 1),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      final encoded = AuthDeniedPayloadCodec.encode(denied);
      expect(
        () => AuthDeniedPayloadCodec.decode([...encoded, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });
}
