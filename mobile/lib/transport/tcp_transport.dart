import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import '../protocol/frame.dart';
import 'frame_buffer.dart';
import 'transport_connection.dart';

/// CTRL mobile TCP client using dart:io. Applies TCP_NODELAY and reassembles
/// frames via [FrameBuffer]; decoded frames flow through [frames].
class TcpTransport implements TransportConnection {
  TcpTransport({
    this._host = '127.0.0.1',
    this._port = defaultPort,
    this._connectTimeout = const Duration(seconds: 5),
  });

  static const int defaultPort = 42123;

  final String _host;
  final int _port;
  final Duration _connectTimeout;

  final StreamController<ProtocolFrame> _framesController =
      StreamController<ProtocolFrame>.broadcast();
  final StreamController<String> _disconnectedController =
      StreamController<String>.broadcast();

  final FrameBuffer _frameBuffer = FrameBuffer();

  Socket? _socket;
  StreamSubscription<List<int>>? _subscription;
  bool _isConnected = false;
  bool _disconnectReported = false;

  @override
  bool get isConnected => _isConnected;

  @override
  Stream<ProtocolFrame> get frames => _framesController.stream;

  @override
  Stream<String> get disconnected => _disconnectedController.stream;

  /// Establishes the TCP connection and starts the read loop.
  Future<void> connect() async {
    final socket = await Socket.connect(
      _host,
      _port,
      timeout: _connectTimeout,
    );
    socket.setOption(SocketOption.tcpNoDelay, true);
    _socket = socket;
    _isConnected = true;

    _subscription = socket.listen(
      (data) {
        _frameBuffer.append(data);
        while (true) {
          final frame = _frameBuffer.tryReadFrame();
          if (frame == null) {
            break;
          }
          _framesController.add(FrameCodec.decode(frame));
        }
      },
      onDone: () {
        if (_frameBuffer.hasBufferedData) {
          _reportDisconnected('Connection closed mid-frame.');
        } else {
          _reportDisconnected('Remote peer closed the connection.');
        }
      },
      onError: (Object error, StackTrace stackTrace) {
        _reportDisconnected('Connection error: $error');
      },
    );
  }

  @override
  Future<void> send(Uint8List frame) async {
    final socket = _socket;
    if (socket == null || !_isConnected) {
      throw StateError('Transport is not connected.');
    }
    socket.add(frame);
    await socket.flush();
  }

  @override
  Future<void> close() async {
    await _subscription?.cancel();
    final socket = _socket;
    _socket = null;
    if (socket != null) {
      socket.destroy();
    }
    _reportDisconnected('Connection closed.');
    if (!_framesController.isClosed) {
      await _framesController.close();
    }
    if (!_disconnectedController.isClosed) {
      await _disconnectedController.close();
    }
  }

  void _reportDisconnected(String reason) {
    if (_disconnectReported || _disconnectedController.isClosed) {
      return;
    }
    _disconnectReported = true;
    _isConnected = false;
    _disconnectedController.add(reason);
  }
}
