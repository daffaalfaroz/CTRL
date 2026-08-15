namespace CTRL.Desktop.Protocol;

public sealed record InputSnapshotPayload(IReadOnlyList<InputEvent> Events);

public static class InputSnapshotPayloadCodec
{
    public const ushort MinEntries = 1;
    public const ushort MaxEntries = 1024;

    public static byte[] Encode(InputSnapshotPayload payload)
    {
        if (payload.Events.Count < MinEntries || payload.Events.Count > MaxEntries)
            throw new ProtocolException("INPUT_SNAPSHOT must contain 1..1024 entries.");
        var writer = new PayloadWriter();
        writer.WriteUInt16((ushort)payload.Events.Count);
        foreach (var e in payload.Events)
            InputEventCodec.EncodeTo(e, writer);
        return writer.ToArray();
    }

    public static InputSnapshotPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var count = reader.ReadUInt16();
        if (count < MinEntries || count > MaxEntries)
            throw new ProtocolException("INPUT_SNAPSHOT must contain 1..1024 entries.");
        var events = new List<InputEvent>(count);
        for (var i = 0; i < count; i++)
            events.Add(InputEventCodec.DecodeFrom(reader));
        reader.ExpectEnd();
        return new InputSnapshotPayload(events);
    }
}
