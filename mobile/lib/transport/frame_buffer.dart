import 'dart:typed_data';

import '../protocol/frame.dart';

/// Incremental frame reassembly buffer for the CTRL protocol framing
/// (fixed 18-byte header + PayloadLength bytes payload, no delimiters).
/// Only performs reassembly; semantic validation stays in [FrameCodec].
class FrameBuffer {
  static const int maxFrameSize =
      ProtocolFrame.headerSize + ProtocolFrame.maxPayloadLength;

  Uint8List _buffer = Uint8List(maxFrameSize);
  int _start = 0;
  int _end = 0;

  /// True when bytes remain buffered that have not been consumed yet.
  bool get hasBufferedData => _start < _end;

  /// Appends bytes received from the socket. Grows the backing buffer only
  /// as data actually arrives; never pre-allocates based on network length.
  void append(List<int> data) {
    _ensureCapacity(data.length);
    _buffer.setRange(_end, _end + data.length, data);
    _end += data.length;
    _compactIfNeeded();
  }

  /// Extracts one complete frame if enough bytes are buffered, or null.
  /// May extract several frames over repeated calls when a single TCP read
  /// carried multiple frames.
  Uint8List? tryReadFrame() {
    final available = _end - _start;
    if (available < ProtocolFrame.headerSize) {
      return null;
    }

    final payloadLength = (_buffer[_start + 6] << 8) | _buffer[_start + 7];
    final frameSize = ProtocolFrame.headerSize + payloadLength;
    if (frameSize > maxFrameSize) {
      throw const ProtocolException(
        'Frame length exceeds maximum allowed size.',
      );
    }

    if (available < frameSize) {
      return null;
    }

    final frame =
        Uint8List.fromList(_buffer.sublist(_start, _start + frameSize));
    _start += frameSize;
    _compactIfNeeded();
    return frame;
  }

  void _ensureCapacity(int extra) {
    var required = (_end - _start) + extra;
    if (required <= _buffer.length) {
      return;
    }

    if (_start > 0) {
      _compact();
      required = (_end - _start) + extra;
      if (required <= _buffer.length) {
        return;
      }
    }

    var newSize = _buffer.length * 2;
    if (newSize < required) {
      newSize = required;
    }
    final grown = Uint8List(newSize);
    grown.setRange(0, _end - _start, _buffer, _start);
    _buffer = grown;
    _end -= _start;
    _start = 0;
  }

  void _compact() {
    final count = _end - _start;
    if (count == 0) {
      _start = 0;
      _end = 0;
      return;
    }
    _buffer.setRange(0, count, _buffer, _start);
    _start = 0;
    _end = count;
  }

  void _compactIfNeeded() {
    if (_start >= 4096 && _start >= (_end - _start)) {
      _compact();
    }
  }
}
