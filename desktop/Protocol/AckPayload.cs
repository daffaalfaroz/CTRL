namespace CTRL.Desktop.Protocol;

public sealed record AckPayload(ushort AckedSequence, ulong AckTime);

public static class AckPayloadCodec
{
    public static byte[] Encode(AckPayload payload)
    {
        var writer = new PayloadWriter();
        writer.WriteUInt16(payload.AckedSequence);
        writer.WriteUInt64(payload.AckTime);
        return writer.ToArray();
    }

    public static AckPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var result = new AckPayload(reader.ReadUInt16(), reader.ReadUInt64());
        reader.ExpectEnd();
        return result;
    }
}
