namespace CTRL.Desktop.Protocol;

public sealed record HelloPayload(
    string DeviceId,
    string ClientVersion,
    byte ProtocolMajor,
    byte ProtocolMinor,
    uint Capabilities);

public static class HelloPayloadCodec
{
    public static byte[] Encode(HelloPayload payload)
    {
        var writer = new PayloadWriter();
        writer.WriteString(payload.DeviceId);
        writer.WriteString(payload.ClientVersion);
        writer.WriteUInt8(payload.ProtocolMajor);
        writer.WriteUInt8(payload.ProtocolMinor);
        writer.WriteUInt32(payload.Capabilities);
        return writer.ToArray();
    }

    public static HelloPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var result = new HelloPayload(
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadUInt8(),
            reader.ReadUInt8(),
            reader.ReadUInt32());
        reader.ExpectEnd();
        return result;
    }
}
