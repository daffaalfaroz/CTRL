namespace CTRL.Desktop.Protocol;

public sealed record PongPayload(ulong ClientSendTime, ulong ServerTime);

public static class PongPayloadCodec
{
    public static byte[] Encode(PongPayload payload)
    {
        var writer = new PayloadWriter();
        writer.WriteUInt64(payload.ClientSendTime);
        writer.WriteUInt64(payload.ServerTime);
        return writer.ToArray();
    }

    public static PongPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var clientSendTime = reader.ReadUInt64();
        var serverTime = reader.ReadUInt64();
        reader.ExpectEnd();
        return new PongPayload(clientSendTime, serverTime);
    }
}
