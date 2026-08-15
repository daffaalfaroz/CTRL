import 'package:flutter_test/flutter_test.dart';
import 'package:ctrl_mobile/protocol/disconnect_payload.dart';
import 'package:ctrl_mobile/protocol/error_payload.dart';
import 'package:ctrl_mobile/protocol/frame.dart';
import 'package:ctrl_mobile/protocol/heartbeat_payload.dart';
import 'package:ctrl_mobile/protocol/pong_payload.dart';

void main() {
  group('ErrorPayloadCodec', () {
    const error = ErrorPayload(
      code: errorCodeAuthFailed,
      severity: errorSeverityWarn,
      message: 'auth failed',
    );

    test('encodes expected wire bytes from docs/protocol.md 19.11', () {
      const expected = <int>[
        0x02, 0x01, 0x00, 0x0B,
        0x61, 0x75, 0x74, 0x68, 0x20, 0x66, 0x61, 0x69, 0x6C, 0x65, 0x64,
      ];
      expect(ErrorPayloadCodec.encode(error), expected);
    });

    test('round-trips', () {
      final decoded = ErrorPayloadCodec.decode(ErrorPayloadCodec.encode(error));
      expect(decoded, error);
    });

    test('accepts every defined code', () {
      for (final code in [
        errorCodeProtocolVersionMismatch,
        errorCodeAuthFailed,
        errorCodeNotAuthenticated,
        errorCodeDeviceLimit,
        errorCodePayloadTooLarge,
        errorCodeInvalidMessage,
        errorCodeUnsupportedMessage,
        errorCodeForbidden,
        errorCodeServerShutdown,
        errorCodeInternal,
      ]) {
        final payload = ErrorPayload(code: code, severity: 0, message: 'x');
        expect(
          ErrorPayloadCodec.decode(ErrorPayloadCodec.encode(payload)),
          payload,
        );
      }
    });

    test('accepts every defined severity', () {
      for (final severity in [
        errorSeverityInfo,
        errorSeverityWarn,
        errorSeverityFatal,
      ]) {
        final payload = ErrorPayload(code: 0x01, severity: severity, message: 'x');
        expect(
          ErrorPayloadCodec.decode(ErrorPayloadCodec.encode(payload)),
          payload,
        );
      }
    });

    test('round-trips empty message', () {
      const payload = ErrorPayload(
        code: errorCodeProtocolVersionMismatch,
        severity: errorSeverityInfo,
        message: '',
      );
      final bytes = ErrorPayloadCodec.encode(payload);
      expect(bytes.sublist(2, 4), [0x00, 0x00]);
      expect(ErrorPayloadCodec.decode(bytes), payload);
    });

    test('messageLength is uint16 BE UTF-8 byte length', () {
      final bytes = ErrorPayloadCodec.encode(
        const ErrorPayload(code: 0x01, severity: 0x00, message: 'échec'),
      );
      expect(bytes.sublist(2, 4), [0x00, 0x06]);
    });

    test('message of 1024 bytes encodes and round-trips', () {
      final payload = ErrorPayload(
        code: errorCodeInternal,
        severity: errorSeverityFatal,
        message: 'a' * 1024,
      );
      final encoded = ErrorPayloadCodec.encode(payload);
      expect(encoded.sublist(2, 4), [0x04, 0x00]);
      expect(ErrorPayloadCodec.decode(encoded), payload);
    });

    test('rejects message over 1024 bytes on encode', () {
      expect(
        () => ErrorPayloadCodec.encode(
          ErrorPayload(
            code: errorCodeInternal,
            severity: errorSeverityFatal,
            message: 'a' * 1025,
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects unknown code on decode', () {
      final encoded = ErrorPayloadCodec.encode(error);
      encoded[0] = 0x0A;
      expect(
        () => ErrorPayloadCodec.decode(encoded),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects unknown code on encode', () {
      expect(
        () => ErrorPayloadCodec.encode(
          const ErrorPayload(code: 0x0A, severity: 0x00, message: 'x'),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects unknown severity on decode', () {
      final encoded = ErrorPayloadCodec.encode(error);
      encoded[1] = 0x03;
      expect(
        () => ErrorPayloadCodec.decode(encoded),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects unknown severity on encode', () {
      expect(
        () => ErrorPayloadCodec.encode(
          const ErrorPayload(code: 0x01, severity: 0x03, message: 'x'),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects message over 1024 bytes on decode', () {
      final overLimit = <int>[
        0x02, 0x01, 0x04, 0x01,
        ...List.filled(1025, 0x61),
      ];
      expect(
        () => ErrorPayloadCodec.decode(overLimit),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid UTF-8 message', () {
      expect(
        () => ErrorPayloadCodec.decode([0x02, 0x01, 0x00, 0x01, 0xFF]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects truncated payload', () {
      final encoded = ErrorPayloadCodec.encode(error);
      expect(
        () => ErrorPayloadCodec.decode(encoded.sublist(0, encoded.length - 1)),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      final encoded = ErrorPayloadCodec.encode(error);
      expect(
        () => ErrorPayloadCodec.decode([...encoded, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });

  group('DisconnectPayloadCodec', () {
    test('encodes expected wire bytes from docs/protocol.md 19.10', () {
      const reason = DisconnectPayload(reason: disconnectReasonNormal);
      expect(DisconnectPayloadCodec.encode(reason), [0x00]);
    });

    test('accepts every defined reason', () {
      for (final reason in [
        disconnectReasonNormal,
        disconnectReasonAppClosing,
        disconnectReasonServerRestart,
        disconnectReasonIdleTimeout,
        disconnectReasonSecurity,
        disconnectReasonProtocolViolation,
      ]) {
        final payload = DisconnectPayload(reason: reason);
        expect(
          DisconnectPayloadCodec.decode(DisconnectPayloadCodec.encode(payload)),
          payload,
        );
      }
    });

    test('rejects unknown reason on decode', () {
      expect(
        () => DisconnectPayloadCodec.decode([0x06]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects unknown reason on encode', () {
      expect(
        () => DisconnectPayloadCodec.encode(
          const DisconnectPayload(reason: 0x06),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects truncated payload', () {
      expect(
        () => DisconnectPayloadCodec.decode([]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      expect(
        () => DisconnectPayloadCodec.decode([0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });

  group('HeartbeatPayloadCodec', () {
    const heartbeat = HeartbeatPayload(clientSendTime: 0x0000018D9E8E2C00);

    test('encodes expected wire bytes from docs/protocol.md 19.9', () {
      const expected = <int>[
        0x00, 0x00, 0x01, 0x8D, 0x9E, 0x8E, 0x2C, 0x00,
      ];
      expect(HeartbeatPayloadCodec.encode(heartbeat), expected);
    });

    test('round-trips lossless', () {
      final decoded =
          HeartbeatPayloadCodec.decode(HeartbeatPayloadCodec.encode(heartbeat));
      expect(decoded, heartbeat);
    });

    test('rejects truncated payload', () {
      final encoded = HeartbeatPayloadCodec.encode(heartbeat);
      expect(
        () => HeartbeatPayloadCodec.decode(encoded.sublist(0, 7)),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      final encoded = HeartbeatPayloadCodec.encode(heartbeat);
      expect(
        () => HeartbeatPayloadCodec.decode([...encoded, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });

  group('PongPayloadCodec', () {
    const pong = PongPayload(
      clientSendTime: 0x0000018D9E8E2C00,
      serverTime: 0x0000018D9E8E2C05,
    );

    test('encodes expected wire bytes from docs/protocol.md 19.9', () {
      const expected = <int>[
        0x00, 0x00, 0x01, 0x8D, 0x9E, 0x8E, 0x2C, 0x00,
        0x00, 0x00, 0x01, 0x8D, 0x9E, 0x8E, 0x2C, 0x05,
      ];
      expect(PongPayloadCodec.encode(pong), expected);
    });

    test('round-trips lossless', () {
      final decoded = PongPayloadCodec.decode(PongPayloadCodec.encode(pong));
      expect(decoded, pong);
    });

    test('rejects truncated payload', () {
      final encoded = PongPayloadCodec.encode(pong);
      expect(
        () => PongPayloadCodec.decode(encoded.sublist(0, encoded.length - 1)),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      final encoded = PongPayloadCodec.encode(pong);
      expect(
        () => PongPayloadCodec.decode([...encoded, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });
}
