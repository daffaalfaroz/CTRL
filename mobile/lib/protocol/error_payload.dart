import 'dart:convert';
import 'dart:typed_data';

import 'frame.dart';
import 'payload_codec.dart';

const int errorCodeProtocolVersionMismatch = 0x01;
const int errorCodeAuthFailed = 0x02;
const int errorCodeNotAuthenticated = 0x03;
const int errorCodeDeviceLimit = 0x04;
const int errorCodePayloadTooLarge = 0x05;
const int errorCodeInvalidMessage = 0x06;
const int errorCodeUnsupportedMessage = 0x07;
const int errorCodeForbidden = 0x08;
const int errorCodeServerShutdown = 0x09;
const int errorCodeInternal = 0xFF;

const int errorSeverityInfo = 0x00;
const int errorSeverityWarn = 0x01;
const int errorSeverityFatal = 0x02;

const int errorMaxMessageLength = 1024;

class ErrorPayload {
  const ErrorPayload({
    required this.code,
    required this.severity,
    required this.message,
  });

  final int code;
  final int severity;
  final String message;

  @override
  bool operator ==(Object other) =>
      other is ErrorPayload &&
      code == other.code &&
      severity == other.severity &&
      message == other.message;

  @override
  int get hashCode => Object.hash(code, severity, message);
}

class ErrorPayloadCodec {
  static Uint8List encode(ErrorPayload payload) {
    if (!_isKnownCode(payload.code)) {
      throw const ProtocolException('Unknown ERROR code.');
    }
    if (!_isKnownSeverity(payload.severity)) {
      throw const ProtocolException('Unknown ERROR severity.');
    }

    final messageBytes = utf8.encode(payload.message);
    if (messageBytes.length > errorMaxMessageLength) {
      throw const ProtocolException('ERROR message exceeds 1024 bytes.');
    }

    final writer = PayloadWriter()
      ..writeUInt8(payload.code)
      ..writeUInt8(payload.severity)
      ..writeUInt16(messageBytes.length)
      ..writeBytes(messageBytes);
    return writer.toBytes();
  }

  static ErrorPayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final code = reader.readUInt8();
    final severity = reader.readUInt8();
    final messageLength = reader.readUInt16();
    if (messageLength > errorMaxMessageLength) {
      throw const ProtocolException('ERROR message exceeds 1024 bytes.');
    }
    final message = reader.readStringOfLength(messageLength);
    reader.expectEnd();

    if (!_isKnownCode(code)) {
      throw const ProtocolException('Unknown ERROR code.');
    }
    if (!_isKnownSeverity(severity)) {
      throw const ProtocolException('Unknown ERROR severity.');
    }

    return ErrorPayload(code: code, severity: severity, message: message);
  }

  static bool _isKnownCode(int code) =>
      code == errorCodeProtocolVersionMismatch ||
      code == errorCodeAuthFailed ||
      code == errorCodeNotAuthenticated ||
      code == errorCodeDeviceLimit ||
      code == errorCodePayloadTooLarge ||
      code == errorCodeInvalidMessage ||
      code == errorCodeUnsupportedMessage ||
      code == errorCodeForbidden ||
      code == errorCodeServerShutdown ||
      code == errorCodeInternal;

  static bool _isKnownSeverity(int severity) => severity <= errorSeverityFatal;
}
