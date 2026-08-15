import 'dart:typed_data';

import 'frame.dart';
import 'input_event.dart';
import 'payload_codec.dart';

class InputSnapshotPayload {
  const InputSnapshotPayload({required this.events});

  final List<InputEvent> events;

  @override
  bool operator ==(Object other) =>
      other is InputSnapshotPayload &&
      events.length == other.events.length &&
      _eventsEqual(events, other.events);

  @override
  int get hashCode => Object.hashAll(events);
}

class InputSnapshotPayloadCodec {
  static const int minEntries = 1;
  static const int maxEntries = 1024;

  static Uint8List encode(InputSnapshotPayload payload) {
    if (payload.events.length < minEntries || payload.events.length > maxEntries) {
      throw const ProtocolException(
        'INPUT_SNAPSHOT must contain 1..1024 entries.',
      );
    }
    final writer = PayloadWriter()..writeUInt16(payload.events.length);
    for (final event in payload.events) {
      InputEventCodec.encodeTo(event, writer);
    }
    return writer.toBytes();
  }

  static InputSnapshotPayload decode(List<int> bytes) {
    final reader = PayloadReader(bytes);
    final count = reader.readUInt16();
    if (count < minEntries || count > maxEntries) {
      throw const ProtocolException(
        'INPUT_SNAPSHOT must contain 1..1024 entries.',
      );
    }
    final events = <InputEvent>[];
    for (var i = 0; i < count; i++) {
      events.add(InputEventCodec.decodeFrom(reader));
    }
    reader.expectEnd();
    return InputSnapshotPayload(events: events);
  }
}

bool _eventsEqual(List<InputEvent> a, List<InputEvent> b) {
  for (var i = 0; i < a.length; i++) {
    if (a[i] != b[i]) {
      return false;
    }
  }
  return true;
}
