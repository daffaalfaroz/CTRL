import 'package:flutter_test/flutter_test.dart';
import 'package:ctrl_mobile/protocol/ack_payload.dart';
import 'package:ctrl_mobile/protocol/frame.dart';
import 'package:ctrl_mobile/protocol/input_event.dart';
import 'package:ctrl_mobile/protocol/input_event_payload.dart';
import 'package:ctrl_mobile/protocol/input_reset_payload.dart';
import 'package:ctrl_mobile/protocol/input_snapshot_payload.dart';

void main() {
  group('AckPayloadCodec', () {
    const ack = AckPayload(ackedSequence: 0x1234, ackTime: 0x0000018D9E8E2C00);

    test('encodes expected wire bytes from docs/protocol.md 11', () {
      const expected = <int>[
        0x12, 0x34,
        0x00, 0x00, 0x01, 0x8D, 0x9E, 0x8E, 0x2C, 0x00,
      ];
      expect(AckPayloadCodec.encode(ack), expected);
    });

    test('round-trips', () {
      final decoded = AckPayloadCodec.decode(AckPayloadCodec.encode(ack));
      expect(decoded, ack);
    });

    test('round-trips wrapped sequence', () {
      const wrapped = AckPayload(ackedSequence: 0xFFFF, ackTime: 0x0000018D9E8E2C05);
      expect(
        AckPayloadCodec.decode(AckPayloadCodec.encode(wrapped)),
        wrapped,
      );
    });

    test('rejects truncated payload', () {
      final encoded = AckPayloadCodec.encode(ack);
      expect(
        () => AckPayloadCodec.decode(encoded.sublist(0, encoded.length - 1)),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      final encoded = AckPayloadCodec.encode(ack);
      expect(
        () => AckPayloadCodec.decode([...encoded, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects empty payload', () {
      expect(
        () => AckPayloadCodec.decode([]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });

  group('InputEventPayloadCodec', () {
    const button = InputEvent(
      controlId: 'btn-fire',
      kind: inputEventKindButton,
      flags: inputEventFlagStateChanged,
      state: inputEventStateDown,
      pressCount: 1,
    );

    const axis = InputEvent(
      controlId: 'thr',
      kind: inputEventKindAxis,
      flags: inputEventFlagStateChanged,
      value: 0.5,
    );

    const stick = InputEvent(
      controlId: 'rs',
      kind: inputEventKindStick,
      flags: inputEventFlagStateChanged,
      x: -0.5,
      y: 0.25,
    );

    const trigger = InputEvent(
      controlId: 't1',
      kind: inputEventKindTrigger,
      flags: inputEventFlagStateChanged,
      value: 1.0,
    );

    const hat = InputEvent(
      controlId: 'dpad',
      kind: inputEventKindHat,
      flags: inputEventFlagStateChanged,
      hatValue: 1,
    );

    test('button encodes expected wire bytes from docs/protocol.md 19.5', () {
      const expected = <int>[
        0x08,
        0x62, 0x74, 0x6E, 0x2D, 0x66, 0x69, 0x72, 0x65,
        0x00, 0x01, 0x01, 0x00, 0x01,
      ];
      expect(InputEventPayloadCodec.encode(const InputEventPayload(event: button)), expected);
    });

    test('axis encodes expected wire bytes from docs/protocol.md 19.6', () {
      const expected = <int>[
        0x03, 0x74, 0x68, 0x72,
        0x01, 0x01,
        0x3F, 0x00, 0x00, 0x00,
      ];
      expect(InputEventPayloadCodec.encode(const InputEventPayload(event: axis)), expected);
    });

    test('stick encodes expected wire bytes from docs/protocol.md 19.7', () {
      const expected = <int>[
        0x02, 0x72, 0x73,
        0x02, 0x01,
        0xBF, 0x00, 0x00, 0x00,
        0x3E, 0x80, 0x00, 0x00,
      ];
      expect(InputEventPayloadCodec.encode(const InputEventPayload(event: stick)), expected);
    });

    test('trigger encodes expected wire bytes from docs/protocol.md 9', () {
      const expected = <int>[
        0x02, 0x74, 0x31,
        0x03, 0x01,
        0x3F, 0x80, 0x00, 0x00,
      ];
      expect(InputEventPayloadCodec.encode(const InputEventPayload(event: trigger)), expected);
    });

    test('hat encodes expected wire bytes from docs/protocol.md 9', () {
      const expected = <int>[
        0x04, 0x64, 0x70, 0x61, 0x64,
        0x04, 0x01,
        0x01,
      ];
      expect(InputEventPayloadCodec.encode(const InputEventPayload(event: hat)), expected);
    });

    test('round-trips every variant lossless', () {
      for (final event in [button, axis, stick, trigger, hat]) {
        final encoded = InputEventPayloadCodec.encode(InputEventPayload(event: event));
        final decoded = InputEventPayloadCodec.decode(encoded);
        expect(decoded.event, event);
      }
    });

    test('hat value 0..8 all round-trip', () {
      for (var v = 0; v <= 8; v++) {
        final e = InputEvent(
          controlId: 'h',
          kind: inputEventKindHat,
          flags: 0,
          hatValue: v,
        );
        expect(
          InputEventPayloadCodec.decode(
            InputEventPayloadCodec.encode(InputEventPayload(event: e)),
          ).event,
          e,
        );
      }
    });

    test('controlId length is UTF-8 byte length', () {
      final e = InputEvent(
        controlId: 'é',
        kind: inputEventKindButton,
        flags: 0,
        state: inputEventStateUp,
        pressCount: 0,
      );
      final encoded = InputEventPayloadCodec.encode(InputEventPayload(event: e));
      expect(encoded[0], 2);
    });

    test('64-byte controlId boundary round-trips', () {
      final e = InputEvent(
        controlId: 'a' * 64,
        kind: inputEventKindButton,
        flags: 0,
        state: inputEventStateUp,
        pressCount: 0,
      );
      expect(
        InputEventPayloadCodec.decode(InputEventPayloadCodec.encode(InputEventPayload(event: e))).event,
        e,
      );
    });

    test('rejects controlId over 64 bytes on encode', () {
      expect(
        () => InputEventPayloadCodec.encode(
          InputEventPayload(
            event: InputEvent(
              controlId: 'a' * 65,
              kind: inputEventKindButton,
              flags: 0,
              state: inputEventStateUp,
              pressCount: 0,
            ),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects controlId over 64 bytes on decode', () {
      expect(
        () => InputEventPayloadCodec.decode([
          0x41,
          ...List.filled(65, 0x61),
          0x00, 0x00, 0x00, 0x00, 0x00,
        ]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects zero-length controlId', () {
      expect(
        () => InputEventPayloadCodec.decode([0x00, 0x00, 0x00, 0x00, 0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid UTF-8 controlId', () {
      expect(
        () => InputEventPayloadCodec.decode([0x01, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects unknown kind on encode', () {
      expect(
        () => InputEventPayloadCodec.encode(
          const InputEventPayload(event: InputEvent(controlId: 'a', kind: 0x05, flags: 0)),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects unknown kind on decode', () {
      expect(
        () => InputEventPayloadCodec.decode([0x01, 0x61, 0x05, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid flags on encode', () {
      expect(
        () => InputEventPayloadCodec.encode(
          InputEventPayload(
            event: InputEvent(
              controlId: 'a',
              kind: inputEventKindButton,
              flags: 0x04,
              state: inputEventStateUp,
              pressCount: 0,
            ),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid flags on decode', () {
      expect(
        () => InputEventPayloadCodec.decode([0x01, 0x61, 0x00, 0x04, 0x00, 0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid button state on encode', () {
      expect(
        () => InputEventPayloadCodec.encode(
          InputEventPayload(
            event: InputEvent(
              controlId: 'a',
              kind: inputEventKindButton,
              flags: 0,
              state: 0x02,
              pressCount: 0,
            ),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid button state on decode', () {
      expect(
        () => InputEventPayloadCodec.decode([0x01, 0x61, 0x00, 0x00, 0x02, 0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects NaN axis on encode and decode', () {
      expect(
        () => InputEventPayloadCodec.encode(
          InputEventPayload(
            event: InputEvent(
              controlId: 'a',
              kind: inputEventKindAxis,
              flags: 0,
              value: double.nan,
            ),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
      expect(
        () => InputEventPayloadCodec.decode([0x01, 0x61, 0x01, 0x00, 0x7F, 0xC0, 0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects infinity on decode', () {
      expect(
        () => InputEventPayloadCodec.decode([0x01, 0x61, 0x01, 0x00, 0x7F, 0x80, 0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects axis out of range', () {
      expect(
        () => InputEventPayloadCodec.encode(
          InputEventPayload(
            event: InputEvent(
              controlId: 'a',
              kind: inputEventKindAxis,
              flags: 0,
              value: 1.5,
            ),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
      expect(
        () => InputEventPayloadCodec.decode([0x01, 0x61, 0x01, 0x00, 0x3F, 0xC0, 0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
      expect(
        () => InputEventPayloadCodec.encode(
          InputEventPayload(
            event: InputEvent(
              controlId: 'a',
              kind: inputEventKindAxis,
              flags: 0,
              value: -0.01,
            ),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
      expect(
        () => InputEventPayloadCodec.decode([0x01, 0x61, 0x01, 0x00, 0xBC, 0x23, 0xD7, 0x0A]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects stick out of range', () {
      expect(
        () => InputEventPayloadCodec.encode(
          InputEventPayload(
            event: InputEvent(
              controlId: 'a',
              kind: inputEventKindStick,
              flags: 0,
              x: -1.01,
              y: 0,
            ),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
      expect(
        () => InputEventPayloadCodec.decode([
          0x01, 0x61, 0x02, 0x00,
          0xBF, 0x81, 0x47, 0xAE,
          0x00, 0x00, 0x00, 0x00,
        ]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid hat value', () {
      expect(
        () => InputEventPayloadCodec.encode(
          const InputEventPayload(
            event: InputEvent(
              controlId: 'a',
              kind: inputEventKindHat,
              flags: 0,
              hatValue: 9,
            ),
          ),
        ),
        throwsA(isA<ProtocolException>()),
      );
      expect(
        () => InputEventPayloadCodec.decode([0x01, 0x61, 0x04, 0x00, 0x09]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects truncated payloads', () {
      final encoded = InputEventPayloadCodec.encode(
        const InputEventPayload(event: button),
      );
      expect(
        () => InputEventPayloadCodec.decode(encoded.sublist(0, encoded.length - 1)),
        throwsA(isA<ProtocolException>()),
      );
      final axisEncoded = InputEventPayloadCodec.encode(
        const InputEventPayload(event: axis),
      );
      expect(
        () => InputEventPayloadCodec.decode(axisEncoded.sublist(0, axisEncoded.length - 1)),
        throwsA(isA<ProtocolException>()),
      );
      final stickEncoded = InputEventPayloadCodec.encode(
        const InputEventPayload(event: stick),
      );
      expect(
        () => InputEventPayloadCodec.decode(stickEncoded.sublist(0, stickEncoded.length - 1)),
        throwsA(isA<ProtocolException>()),
      );
      final triggerEncoded = InputEventPayloadCodec.encode(
        const InputEventPayload(event: trigger),
      );
      expect(
        () => InputEventPayloadCodec.decode(triggerEncoded.sublist(0, triggerEncoded.length - 1)),
        throwsA(isA<ProtocolException>()),
      );
      final hatEncoded = InputEventPayloadCodec.encode(
        const InputEventPayload(event: hat),
      );
      expect(
        () => InputEventPayloadCodec.decode(hatEncoded.sublist(0, hatEncoded.length - 1)),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      final encoded = InputEventPayloadCodec.encode(
        const InputEventPayload(event: button),
      );
      expect(
        () => InputEventPayloadCodec.decode([...encoded, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });

  group('InputSnapshotPayloadCodec', () {
    const snapshot = InputSnapshotPayload(events: [
      InputEvent(
        controlId: 'btn-fire',
        kind: inputEventKindButton,
        flags: inputEventFlagStateChanged,
        state: inputEventStateDown,
        pressCount: 5,
      ),
      InputEvent(
        controlId: 'rs',
        kind: inputEventKindStick,
        flags: inputEventFlagStateChanged,
        x: 0,
        y: 0,
      ),
    ]);

    test('encodes expected wire bytes from docs/protocol.md 19.8', () {
      const expected = <int>[
        0x00, 0x02,
        0x08,
        0x62, 0x74, 0x6E, 0x2D, 0x66, 0x69, 0x72, 0x65,
        0x00, 0x01, 0x01, 0x00, 0x05,
        0x02, 0x72, 0x73,
        0x02, 0x01,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
      ];
      expect(InputSnapshotPayloadCodec.encode(snapshot), expected);
    });

    test('round-trips', () {
      final decoded = InputSnapshotPayloadCodec.decode(
        InputSnapshotPayloadCodec.encode(snapshot),
      );
      expect(decoded, snapshot);
    });

    test('single entry round-trips', () {
      const single = InputSnapshotPayload(events: [
        InputEvent(
          controlId: 'a',
          kind: inputEventKindButton,
          flags: 0,
          state: inputEventStateUp,
          pressCount: 0,
        ),
      ]);
      expect(
        InputSnapshotPayloadCodec.decode(InputSnapshotPayloadCodec.encode(single)),
        single,
      );
    });

    test('rejects empty list on encode', () {
      expect(
        () => InputSnapshotPayloadCodec.encode(const InputSnapshotPayload(events: [])),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects over 1024 entries on encode', () {
      final many = List<InputEvent>.generate(
        1025,
        (_) => const InputEvent(
          controlId: 'a',
          kind: inputEventKindButton,
          flags: 0,
          state: inputEventStateUp,
          pressCount: 0,
        ),
      );
      expect(
        () => InputSnapshotPayloadCodec.encode(InputSnapshotPayload(events: many)),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects zero entryCount on decode', () {
      expect(
        () => InputSnapshotPayloadCodec.decode([0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects over-1024 entryCount on decode before reading events', () {
      expect(
        () => InputSnapshotPayloadCodec.decode([
          0x04, 0x01,
          0x01, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00,
        ]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects invalid UTF-8 controlId inside an entry', () {
      expect(
        () => InputSnapshotPayloadCodec.decode([
          0x00, 0x01,
          0x01, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00,
        ]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects truncated payload', () {
      final encoded = InputSnapshotPayloadCodec.encode(snapshot);
      expect(
        () => InputSnapshotPayloadCodec.decode(encoded.sublist(0, encoded.length - 1)),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      final encoded = InputSnapshotPayloadCodec.encode(snapshot);
      expect(
        () => InputSnapshotPayloadCodec.decode([...encoded, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });

  group('InputResetPayloadCodec', () {
    test('encodes expected wire bytes from docs/protocol.md 16', () {
      expect(
        InputResetPayloadCodec.encode(const InputResetPayload(reason: inputResetReasonStateReset)),
        [0x00],
      );
    });

    test('round-trips every reason', () {
      for (final reason in [
        inputResetReasonStateReset,
        inputResetReasonProfileSwitch,
        inputResetReasonMaintenance,
      ]) {
        final payload = InputResetPayload(reason: reason);
        expect(
          InputResetPayloadCodec.decode(InputResetPayloadCodec.encode(payload)),
          payload,
        );
      }
    });

    test('rejects unknown reason on encode', () {
      expect(
        () => InputResetPayloadCodec.encode(const InputResetPayload(reason: 0x03)),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects unknown reason on decode', () {
      expect(
        () => InputResetPayloadCodec.decode([0x03]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects truncated payload', () {
      expect(
        () => InputResetPayloadCodec.decode([]),
        throwsA(isA<ProtocolException>()),
      );
    });

    test('rejects extra bytes', () {
      expect(
        () => InputResetPayloadCodec.decode([0x00, 0x00]),
        throwsA(isA<ProtocolException>()),
      );
    });
  });
}
