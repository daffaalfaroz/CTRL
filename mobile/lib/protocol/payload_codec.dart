import 'dart:convert';
import 'dart:typed_data';

import 'frame.dart';

class PayloadWriter {
  final List<int> _bytes = <int>[];

  int get length => _bytes.length;

  void writeUInt8(int value) {
    _checkRange(value, 0, 0xFF, 'uint8');
    _bytes.add(value);
  }

  void writeUInt16(int value) {
    _checkRange(value, 0, 0xFFFF, 'uint16');
    _bytes
      ..add((value >> 8) & 0xFF)
      ..add(value & 0xFF);
  }

  void writeUInt32(int value) {
    _checkRange(value, 0, 0xFFFFFFFF, 'uint32');
    _bytes
      ..add((value >> 24) & 0xFF)
      ..add((value >> 16) & 0xFF)
      ..add((value >> 8) & 0xFF)
      ..add(value & 0xFF);
  }

  void writeUInt64(int value) {
    if (value < 0) {
      throw const ProtocolException('uint64 out of range.');
    }
    for (var shift = 56; shift >= 0; shift -= 8) {
      _bytes.add((value >> shift) & 0xFF);
    }
  }

  void writeFloat32(double value) {
    final data = ByteData(4)..setFloat32(0, value, Endian.big);
    for (var i = 0; i < 4; i++) {
      _bytes.add(data.getUint8(i));
    }
  }

  void writeBytes(Uint8List bytes) {
    _bytes.addAll(bytes);
  }

  void writeString(String value, {int minLength = 0, int maxLength = 0xFF}) {
    final bytes = utf8.encode(value);
    if (bytes.length < minLength || bytes.length > maxLength) {
      throw ProtocolException(
          'String must be $minLength-$maxLength UTF-8 bytes.');
    }
    _bytes
      ..add(bytes.length)
      ..addAll(bytes);
  }

  Uint8List toBytes() => Uint8List.fromList(_bytes);

  void _checkRange(int value, int min, int max, String name) {
    if (value < min || value > max) {
      throw ProtocolException('$name out of range.');
    }
  }
}

class PayloadReader {
  final Uint8List _data;
  int _offset = 0;

  PayloadReader(List<int> data) : _data = Uint8List.fromList(data);

  int get remaining => _data.length - _offset;

  int readUInt8() {
    _require(1);
    return _data[_offset++];
  }

  int readUInt16() {
    _require(2);
    final value = (_data[_offset] << 8) | _data[_offset + 1];
    _offset += 2;
    return value;
  }

  int readUInt32() {
    _require(4);
    final value = ByteData.sublistView(_data, _offset, _offset + 4)
        .getUint32(0, Endian.big);
    _offset += 4;
    return value;
  }

  int readUInt64() {
    _require(8);
    final value = ByteData.sublistView(_data, _offset, _offset + 8)
        .getUint64(0, Endian.big);
    _offset += 8;
    return value;
  }

  double readFloat32() {
    _require(4);
    final value = ByteData.sublistView(_data, _offset, _offset + 4)
        .getFloat32(0, Endian.big);
    _offset += 4;
    return value;
  }

  Uint8List readBytes(int count) {
    if (count < 0) {
      throw const ProtocolException('Invalid byte count.');
    }
    _require(count);
    final result =
        Uint8List.fromList(_data.sublist(_offset, _offset + count));
    _offset += count;
    return result;
  }

  String readString({int minLength = 0, int maxLength = 0xFF}) {
    final length = readUInt8();
    if (length < minLength || length > maxLength) {
      throw ProtocolException(
          'String must be $minLength-$maxLength UTF-8 bytes.');
    }
    return readStringOfLength(length);
  }

  String readStringOfLength(int length) {
    _require(length);
    final bytes = _data.sublist(_offset, _offset + length);
    _offset += length;
    try {
      return utf8.decode(bytes, allowMalformed: false);
    } on FormatException catch (e) {
      throw ProtocolException('Invalid UTF-8 string: ${e.message}');
    }
  }

  void expectEnd() {
    if (remaining != 0) {
      throw const ProtocolException('Payload contains extra bytes.');
    }
  }

  void _require(int count) {
    if (count > remaining) {
      throw const ProtocolException('Payload is truncated.');
    }
  }
}
