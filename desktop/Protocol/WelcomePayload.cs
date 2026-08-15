namespace CTRL.Desktop.Protocol;

public sealed record WelcomePayload(
    string ServerName,
    byte EffectiveMajor,
    byte EffectiveMinor,
    byte MinSupportedMajor,
    byte[] SessionId,
    bool AuthRequired,
    byte[] Challenge);

public static class WelcomePayloadCodec
{
    public static byte[] Encode(WelcomePayload payload)
    {
        if (payload.SessionId is null || payload.SessionId.Length != 16)
            throw new ProtocolException("sessionId must be exactly 16 bytes.");
        if (payload.Challenge is null || payload.Challenge.Length != 32)
            throw new ProtocolException("challenge must be exactly 32 bytes.");

        var writer = new PayloadWriter();
        writer.WriteString(payload.ServerName, 1, 64);
        writer.WriteUInt8(payload.EffectiveMajor);
        writer.WriteUInt8(payload.EffectiveMinor);
        writer.WriteUInt8(payload.MinSupportedMajor);
        writer.WriteBytes(payload.SessionId);
        writer.WriteUInt8(payload.AuthRequired ? (byte)1 : (byte)0);
        writer.WriteBytes(payload.Challenge);
        return writer.ToArray();
    }

    public static WelcomePayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var serverName = reader.ReadString(1, 64);
        var effectiveMajor = reader.ReadUInt8();
        var effectiveMinor = reader.ReadUInt8();
        var minSupportedMajor = reader.ReadUInt8();
        var sessionId = reader.ReadBytes(16);
        var authRequired = reader.ReadUInt8();
        var challenge = reader.ReadBytes(32);
        reader.ExpectEnd();

        if (authRequired > 1)
            throw new ProtocolException("authRequired must be 0 or 1.");

        return new WelcomePayload(
            serverName,
            effectiveMajor,
            effectiveMinor,
            minSupportedMajor,
            sessionId,
            authRequired == 1,
            challenge);
    }
}
