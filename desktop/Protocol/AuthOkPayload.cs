namespace CTRL.Desktop.Protocol;

public sealed record AuthOkPayload(
    byte Result,
    byte[] SessionId,
    uint ServerCapabilities,
    byte[] NewToken);

public static class AuthOkPayloadCodec
{
    public const byte ResultOk = 0x00;

    public static byte[] Encode(AuthOkPayload payload)
    {
        if (payload.Result != ResultOk)
            throw new ProtocolException("Unknown AUTH_OK result.");
        if (payload.SessionId is null || payload.SessionId.Length != 16)
            throw new ProtocolException("sessionId must be exactly 16 bytes.");
        if (payload.NewToken is null)
            throw new ProtocolException("newToken must not be null.");
        if (payload.NewToken.Length > byte.MaxValue)
            throw new ProtocolException("newToken must not exceed 255 bytes.");

        var writer = new PayloadWriter();
        writer.WriteUInt8(payload.Result);
        writer.WriteBytes(payload.SessionId);
        writer.WriteUInt32(payload.ServerCapabilities);
        writer.WriteUInt8((byte)payload.NewToken.Length);
        writer.WriteBytes(payload.NewToken);
        return writer.ToArray();
    }

    public static AuthOkPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var result = reader.ReadUInt8();
        var sessionId = reader.ReadBytes(16);
        var serverCapabilities = reader.ReadUInt32();
        var newTokenLength = reader.ReadUInt8();
        var newToken = reader.ReadBytes(newTokenLength);
        reader.ExpectEnd();

        if (result != ResultOk)
            throw new ProtocolException("Unknown AUTH_OK result.");

        return new AuthOkPayload(result, sessionId, serverCapabilities, newToken);
    }
}
