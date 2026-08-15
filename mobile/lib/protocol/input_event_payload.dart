import 'dart:typed_data';

import 'input_event.dart';

class InputEventPayload {
  const InputEventPayload({required this.event});

  final InputEvent event;

  @override
  bool operator ==(Object other) =>
      other is InputEventPayload && event == other.event;

  @override
  int get hashCode => event.hashCode;
}

class InputEventPayloadCodec {
  static Uint8List encode(InputEventPayload payload) =>
      InputEventCodec.encode(payload.event);

  static InputEventPayload decode(List<int> bytes) =>
      InputEventPayload(event: InputEventCodec.decode(bytes));
}
