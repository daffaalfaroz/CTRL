namespace CTRL.Desktop.Protocol;

public sealed record HeartbeatPayload(ulong ClientSendTime);

public static class HeartbeatPayloadCodec
{
    public static byte[] Encode(HeartbeatPayload payload)
    {
        var writer = new PayloadWriter();
        writer.WriteUInt64(payload.ClientSendTime);
        return writer.ToArray();
    }

    public static HeartbeatPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var clientSendTime = reader.ReadUInt64();
        reader.ExpectEnd();
        return new HeartbeatPayload(clientSendTime);
    }
}
