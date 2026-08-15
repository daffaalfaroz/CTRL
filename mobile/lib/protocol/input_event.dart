import 'dart:convert';
import 'dart:typed_data';

import 'frame.dart';
import 'payload_codec.dart';

const int inputEventKindButton = 0x00;
const int inputEventKindAxis = 0x01;
const int inputEventKindStick = 0x02;
const int inputEventKindTrigger = 0x03;
const int inputEventKindHat = 0x04;

const int inputEventFlagStateChanged = 0x01;
const int inputEventFlagInitial = 0x02;

const int inputEventStateUp = 0x00;
const int inputEventStateDown = 0x01;

const int inputEventMinControlIdLength = 1;
const int inputEventMaxControlIdLength = 64;

class InputEvent {
  const InputEvent({
    required this.controlId,
    required this.kind,
    required this.flags,
    this.state,
    this.pressCount,
    this.value,
    this.x,
    this.y,
    this.hatValue,
  });

  final String controlId;
  final int kind;
  final int flags;
  final int? state;
  final int? pressCount;
  final double? value;
  final double? x;
  final double? y;
  final int? hatValue;

  @override
  bool operator ==(Object other) =>
      other is InputEvent &&
      controlId == other.controlId &&
      kind == other.kind &&
      flags == other.flags &&
      state == other.state &&
      pressCount == other.pressCount &&
      value == other.value &&
      x == other.x &&
      y == other.y &&
      hatValue == other.hatValue;

  @override
  int get hashCode => Object.hash(
        controlId,
        kind,
        flags,
        state,
        pressCount,
        value,
        x,
        y,
        hatValue,
      );
}

class InputEventCodec {
  static const int _flagsMask = inputEventFlagStateChanged | inputEventFlagInitial;

  static Uint8List encode(InputEvent event) {
    final writer = PayloadWriter();
    encodeTo(event, writer);
    return writer.toBytes();
  }

  static void encodeTo(InputEvent event, PayloadWriter writer) {
    final controlIdBytes = utf8.encode(event.controlId);
    if (controlIdBytes.isEmpty ||
        controlIdBytes.length > inputEventMaxControlIdLength) {
      throw const ProtocolException('controlId must be 1..64 UTF-8 bytes.');
    }
    if (!_isKnownKind(event.kind)) {
      throw const ProtocolException('Unknown input event kind.');
    }
    if ((event.flags & ~_flagsMask) != 0) {
      throw const ProtocolException('Invalid input event flags.');
    }

    writer
      ..writeUInt8(controlIdBytes.length)
      ..writeBytes(controlIdBytes)
      ..writeUInt8(event.kind)
      ..writeUInt8(event.flags);

    switch (event.kind) {
      case inputEventKindButton:
        if (event.state == null ||
            (event.state != inputEventStateUp && event.state != inputEventStateDown)) {
          throw const ProtocolException('Button event requires a valid state.');
        }
        if (event.pressCount == null) {
          throw const ProtocolException('Button event requires a pressCount.');
        }
        writer
          ..writeUInt8(event.state!)
          ..writeUInt16(event.pressCount!);
        break;
      case inputEventKindAxis:
      case inputEventKindTrigger:
        if (event.value == null) {
          throw const ProtocolException('Axis/trigger event requires a value.');
        }
        _writeRangeCheckedFloat(writer, event.value!, 0, 1, 'axis/trigger value');
        break;
      case inputEventKindStick:
        if (event.x == null || event.y == null) {
          throw const ProtocolException('Stick event requires x and y.');
        }
        _writeRangeCheckedFloat(writer, event.x!, -1, 1, 'stick x');
        _writeRangeCheckedFloat(writer, event.y!, -1, 1, 'stick y');
        break;
      case inputEventKindHat:
        if (event.hatValue == null || event.hatValue! > 8) {
          throw const ProtocolException('Hat event requires a value in 0..8.');
        }
        writer.writeUInt8(event.hatValue!);
        break;
    }
  }

  static InputEvent decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final event = decodeFrom(reader);
    reader.expectEnd();
    return event;
  }

  static InputEvent decodeFrom(PayloadReader reader) {
    final controlIdLength = reader.readUInt8();
    if (controlIdLength < inputEventMinControlIdLength ||
        controlIdLength > inputEventMaxControlIdLength) {
      throw const ProtocolException('controlId must be 1..64 UTF-8 bytes.');
    }
    final controlId = reader.readStringOfLength(controlIdLength);
    final kind = reader.readUInt8();
    if (!_isKnownKind(kind)) {
      throw const ProtocolException('Unknown input event kind.');
    }
    final flags = reader.readUInt8();
    if ((flags & ~_flagsMask) != 0) {
      throw const ProtocolException('Invalid input event flags.');
    }

    int? state;
    int? pressCount;
    double? value;
    double? x;
    double? y;
    int? hatValue;

    switch (kind) {
      case inputEventKindButton:
        state = reader.readUInt8();
        if (state != inputEventStateUp && state != inputEventStateDown) {
          throw const ProtocolException('Invalid button state.');
        }
        pressCount = reader.readUInt16();
        break;
      case inputEventKindAxis:
      case inputEventKindTrigger:
        value = _readRangeCheckedFloat(reader, 0, 1, 'axis/trigger value');
        break;
      case inputEventKindStick:
        x = _readRangeCheckedFloat(reader, -1, 1, 'stick x');
        y = _readRangeCheckedFloat(reader, -1, 1, 'stick y');
        break;
      case inputEventKindHat:
        hatValue = reader.readUInt8();
        if (hatValue > 8) {
          throw const ProtocolException('Invalid hat value.');
        }
        break;
    }

    return InputEvent(
      controlId: controlId,
      kind: kind,
      flags: flags,
      state: state,
      pressCount: pressCount,
      value: value,
      x: x,
      y: y,
      hatValue: hatValue,
    );
  }

  static void _writeRangeCheckedFloat(
      PayloadWriter writer, double v, double min, double max, String name) {
    if (v.isNaN || v.isInfinite) {
      throw ProtocolException('$name must be finite.');
    }
    if (v < min || v > max) {
      throw ProtocolException('$name is out of range.');
    }
    writer.writeFloat32(v);
  }

  static double _readRangeCheckedFloat(
      PayloadReader reader, double min, double max, String name) {
    final v = reader.readFloat32();
    if (v.isNaN || v.isInfinite) {
      throw ProtocolException('$name must be finite.');
    }
    if (v < min || v > max) {
      throw ProtocolException('$name is out of range.');
    }
    return v;
  }

  static bool _isKnownKind(int kind) =>
      kind == inputEventKindButton ||
      kind == inputEventKindAxis ||
      kind == inputEventKindStick ||
      kind == inputEventKindTrigger ||
      kind == inputEventKindHat;
}
