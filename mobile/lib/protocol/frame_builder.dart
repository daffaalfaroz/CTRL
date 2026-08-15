import 'dart:typed_data';

import 'frame.dart';

/// Convenience builder for encoded frames. The session layer owns sequence
/// numbers and flags (D1/D2) and uses this instead of letting the codec decide.
/// encode still rejects reserved flags defensively.
class FrameBuilder {
  FrameBuilder._();

  static Uint8List build({
    required int messageType,
    required Uint8List payload,
    required int sequence,
    bool ackRequested = false,
    bool mustUnderstand = false,
    int versionMajor = 1,
    int versionMinor = 0,
    int timestamp = 0,
  }) {
    var flags = 0;
    if (ackRequested) {
      flags |= FrameCodec.ackRequested;
    }
    if (mustUnderstand) {
      flags |= FrameCodec.mustUnderstand;
    }
    return FrameCodec.encode(
      ProtocolFrame(
        versionMajor: versionMajor,
        versionMinor: versionMinor,
        flags: flags,
        messageType: messageType,
        sequence: sequence,
        timestamp: timestamp,
        payload: payload,
      ),
    );
  }
}