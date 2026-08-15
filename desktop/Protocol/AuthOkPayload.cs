namespace CTRL.Desktop.Protocol;

public sealed record AuthOkPayload(
    byte Result,
    byte[] SessionId,
    uint ServerCapabilities,
    string NewToken);

public static class AuthOkPayloadCodec
{
    public const byte ResultOk = 0x00;

    public static byte[] Encode(AuthOkPayload payload)
    {
        if (payload.Result != ResultOk)
            throw new ProtocolException("Unknown AUTH_OK result.");
        if (payload.SessionId is null || payload.SessionId.Length != 16)
            throw new ProtocolException("sessionId must be exactly 16 bytes.");

        var writer = new PayloadWriter();
        writer.WriteUInt8(payload.Result);
        writer.WriteBytes(payload.SessionId);
        writer.WriteUInt32(payload.ServerCapabilities);
        writer.WriteString(payload.NewToken);
        return writer.ToArray();
    }

    public static AuthOkPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var result = reader.ReadUInt8();
        var sessionId = reader.ReadBytes(16);
        var serverCapabilities = reader.ReadUInt32();
        var newToken = reader.ReadString();
        reader.ExpectEnd();

        if (result != ResultOk)
            throw new ProtocolException("Unknown AUTH_OK result.");

        return new AuthOkPayload(result, sessionId, serverCapabilities, newToken);
    }
}
