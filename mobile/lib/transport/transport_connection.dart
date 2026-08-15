import 'dart:typed_data';

import '../protocol/frame.dart';

/// Transport abstraction decoupling the session layer from the underlying
/// channel (TCP today; a UDP channel can implement the same contract later).
abstract class TransportConnection {
  bool get isConnected;

  /// Stream of fully decoded frames received from the peer.
  Stream<ProtocolFrame> get frames;

  /// Stream that emits exactly one reason when the connection is lost.
  Stream<String> get disconnected;

  /// Sends one already-encoded frame (header + payload) to the peer.
  Future<void> send(Uint8List frame);

  /// Closes the connection and releases all resources.
  Future<void> close();
}
