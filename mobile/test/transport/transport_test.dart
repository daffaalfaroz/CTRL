import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';

import 'package:ctrl_mobile/protocol/frame.dart';
import 'package:ctrl_mobile/transport/frame_buffer.dart';
import 'package:ctrl_mobile/transport/tcp_transport.dart';

void main() {
  group('FrameBuffer', () {
    test('reassembles a frame fed byte by byte', () {
      final feed = FrameCodec.encode(_echoFrame(0x0012, [0x10, 0x20]));
      final buffer = FrameBuffer();
      for (var i = 0; i < feed.length - 1; i++) {
        buffer.append([feed[i]]);
        expect(buffer.tryReadFrame(), isNull, reason: 'byte $i');
        expect(buffer.hasBufferedData, isTrue, reason: 'byte $i');
      }
      buffer.append([feed[feed.length - 1]]);
      expect(buffer.tryReadFrame(), feed);
      expect(buffer.hasBufferedData, isFalse);
    });

    test('reassembles a frame with a payload split', () {
      final feed = FrameCodec.encode(_echoFrame(0x0012, [0x10, 0x20]));
      final buffer = FrameBuffer();
      buffer.append(feed.sublist(0, feed.length - 1));
      expect(buffer.tryReadFrame(), isNull);
      expect(buffer.hasBufferedData, isTrue);
      buffer.append(feed.sublist(feed.length - 1));
      expect(buffer.tryReadFrame(), feed);
      expect(buffer.hasBufferedData, isFalse);
    });

    test('reassembles a frame split at every byte position', () {
      final feed = FrameCodec.encode(_echoFrame(0x0012, [0x10, 0x20]));
      for (var split = 0; split <= feed.length; split++) {
        final buffer = FrameBuffer();
        buffer.append(feed.sublist(0, split));
        buffer.append(feed.sublist(split));
        expect(buffer.tryReadFrame(), feed, reason: 'split at $split');
        expect(buffer.hasBufferedData, isFalse, reason: 'split at $split');
      }
    });

    test('extracts multiple frames from a single chunk', () {
      final encoded = [
        FrameCodec.encode(_echoFrame(0x01, [0x11])),
        FrameCodec.encode(_echoFrame(0x02, [0x22])),
        FrameCodec.encode(_echoFrame(0x03, [0x33])),
      ];
      final chunk = <int>[...encoded[0], ...encoded[1], ...encoded[2]];
      final buffer = FrameBuffer();
      buffer.append(chunk);
      expect(buffer.tryReadFrame(), encoded[0]);
      expect(buffer.tryReadFrame(), encoded[1]);
      expect(buffer.tryReadFrame(), encoded[2]);
      expect(buffer.hasBufferedData, isFalse);
    });

    test('keeps a partial next frame buffered after a complete frame', () {
      final one = FrameCodec.encode(_echoFrame(0x01, [0xAA]));
      final two = FrameCodec.encode(_echoFrame(0x02, [0xBB]));
      final buffer = FrameBuffer();
      buffer.append([...one, ...two.sublist(0, 7)]);
      expect(buffer.tryReadFrame(), one);
      expect(buffer.hasBufferedData, isTrue);
      buffer.append(two.sublist(7));
      expect(buffer.tryReadFrame(), two);
      expect(buffer.hasBufferedData, isFalse);
    });

    test('survives 100 frames in one chunk', () {
      final buffer = FrameBuffer();
      for (var i = 0; i < 100; i++) {
        buffer.append(FrameCodec.encode(_echoFrame(0x0001 + i, [i])));
      }
      for (var i = 0; i < 100; i++) {
        expect(buffer.tryReadFrame(), isNotNull);
      }
      expect(buffer.hasBufferedData, isFalse);
    });

    test('handles a maximum-size frame', () {
      final buffer = FrameBuffer();
      final maxFrame = Uint8List(FrameBuffer.maxFrameSize);
      final data = ByteData.sublistView(maxFrame);
      data.setUint16(0, ProtocolFrame.magic, Endian.big);
      data.setUint16(6, ProtocolFrame.maxPayloadLength, Endian.big);
      buffer.append(maxFrame);
      final frame = buffer.tryReadFrame();
      expect(frame, isNotNull);
      expect(frame!.length, FrameBuffer.maxFrameSize);
      expect(buffer.hasBufferedData, isFalse);
    });
  });

  group('TcpTransport loopback', () {
    late ServerSocket serverSocket;

    setUp(() async {
      serverSocket = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    });

    tearDown(() async {
      await serverSocket.close();
    });

    test('round-trips frames and reports disconnect exactly once', () async {
      final serverFrames = <ProtocolFrame>[];
      final serverDisconnects = <String>[];
      final serverBuffer = FrameBuffer();

      final serverDone = Completer<void>();
      serverSocket.listen(
        (socket) {
          socket.setOption(SocketOption.tcpNoDelay, true);
          socket.listen(
            (data) {
              serverBuffer.append(data);
              while (true) {
                final frame = serverBuffer.tryReadFrame();
                if (frame == null) {
                  break;
                }
                serverFrames.add(FrameCodec.decode(frame));
                socket.add(FrameCodec.encode(_echoFrame(0x01, [0x55])));
              }
            },
            onDone: () {
              serverDisconnects.add('remote closed');
              if (!serverDone.isCompleted) {
                serverDone.complete();
              }
            },
          );
        },
      );

      final transport = TcpTransport(port: serverSocket.port);
      await transport.connect();
      expect(transport.isConnected, isTrue);

      final clientFrames = <ProtocolFrame>[];
      final clientDisconnects = <String>[];
      transport.frames.listen(clientFrames.add);
      transport.disconnected.listen(clientDisconnects.add);

      await transport.send(FrameCodec.encode(_echoFrame(0x02, [0x66])));
      await waitUntil(() => clientFrames.length == 1);
      expect(serverFrames.length, 1);
      expect(
        _framesEqual(serverFrames.first, _echoFrame(0x02, [0x66])),
        isTrue,
      );
      expect(
        _framesEqual(clientFrames.first, _echoFrame(0x01, [0x55])),
        isTrue,
      );

      await transport.close();
      await waitUntil(() => serverDone.isCompleted);
      await waitUntil(() => clientDisconnects.length == 1);
      expect(clientDisconnects.length, 1);
      expect(serverDisconnects.length, 1);
      expect(transport.isConnected, isFalse);
    });

    test('reports a mid-frame EOF as an error reason', () async {
      final transport = TcpTransport(port: serverSocket.port);
      final disconnects = <String>[];
      transport.disconnected.listen(disconnects.add);
      await transport.connect();

      serverSocket.listen((socket) {
        socket.add(
          FrameCodec.encode(_echoFrame(0x01, [0xAA])).sublist(0, 5),
        );
        socket.destroy();
      });

      await waitUntil(() => disconnects.length == 1);
      expect(disconnects.single, contains('mid-frame'));
    });

    test('protocol decode error emits exactly one disconnect and cleans up',
        () async {
      final serverClosed = Completer<void>();
      serverSocket.listen((socket) {
        socket.add(_malformedFrame());
        socket.listen(
          (_) {},
          onDone: () {
            if (!serverClosed.isCompleted) {
              serverClosed.complete();
            }
          },
        );
      });

      final transport = TcpTransport(port: serverSocket.port);
      final disconnects = <String>[];
      transport.disconnected.listen(disconnects.add);
      await transport.connect();

      await waitUntil(() => disconnects.length == 1);
      expect(disconnects.single, contains('Protocol error'));
      expect(transport.isConnected, isFalse);

      await waitUntil(() => serverClosed.isCompleted);
      await transport.close();
      expect(disconnects.length, 1);
    });

    test('close after a protocol error does not duplicate the disconnect',
        () async {
      serverSocket.listen((socket) {
        socket.add(_malformedFrame());
      });

      final transport = TcpTransport(port: serverSocket.port);
      final disconnects = <String>[];
      transport.disconnected.listen(disconnects.add);
      await transport.connect();

      await waitUntil(() => disconnects.length == 1);
      await transport.close();
      await transport.close();
      expect(disconnects.length, 1);
    });
  });
}

ProtocolFrame _echoFrame(int sequence, List<int> payload) => ProtocolFrame(
      versionMajor: 0x01,
      versionMinor: 0x00,
      flags: 0,
      messageType: 0xFF,
      sequence: sequence,
      timestamp: 0,
      payload: Uint8List.fromList(payload),
    );

Uint8List _malformedFrame() => Uint8List(ProtocolFrame.headerSize);

bool _framesEqual(ProtocolFrame a, ProtocolFrame b) =>
    a.versionMajor == b.versionMajor &&
    a.versionMinor == b.versionMinor &&
    a.flags == b.flags &&
    a.messageType == b.messageType &&
    a.sequence == b.sequence &&
    a.timestamp == b.timestamp &&
    _payloadEqual(a.payload, b.payload);

bool _payloadEqual(List<int> a, List<int> b) {
  if (a.length != b.length) {
    return false;
  }
  for (var i = 0; i < a.length; i++) {
    if (a[i] != b[i]) {
      return false;
    }
  }
  return true;
}

Future<void> waitUntil(bool Function() condition, {int timeoutMs = 5000}) async {
  final deadline = DateTime.now().add(Duration(milliseconds: timeoutMs));
  while (!condition()) {
    if (DateTime.now().isAfter(deadline)) {
      throw StateError('Timed out waiting for condition.');
    }
    await Future<void>.delayed(const Duration(milliseconds: 10));
  }
}
