import 'dart:async';
import 'dart:typed_data';

import 'package:ctrl_mobile/protocol/frame.dart';
import 'package:ctrl_mobile/transport/transport_connection.dart';

/// Minimal scripted transport for application-layer tests: implements the
/// [TransportConnection] contract so the REAL [ClientSession] can be driven
/// against a fake server inside ConnectionController tests.
class FakeAppTransport extends TransportConnection {
  final List<ProtocolFrame> sentFrames = [];
  final _frames = StreamController<ProtocolFrame>.broadcast();
  final _disconnected = StreamController<String>.broadcast();

  @override
  bool isConnected = true;

  @override
  Stream<ProtocolFrame> get frames => _frames.stream;

  @override
  Stream<String> get disconnected => _disconnected.stream;

  /// Pushes a scripted server frame to the session.
  void emit(ProtocolFrame frame) => _frames.add(frame);

  /// Simulates an unexpected connection loss (drives engine reconnecting).
  void emitDisconnected(String reason) {
    if (isConnected) {
      isConnected = false;
      _disconnected.add(reason);
    }
  }

  @override
  Future<void> send(Uint8List frame) async {
    sentFrames.add(FrameCodec.decode(frame));
  }

  @override
  Future<void> close() async {
    if (isConnected) {
      emitDisconnected('closed by test');
    }
    await _frames.close();
    await _disconnected.close();
  }
}
