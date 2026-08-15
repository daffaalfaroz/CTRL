namespace CTRL.Desktop.Protocol;

public sealed record AuthDeniedPayload(
    byte Reason,
    string Message);

public static class AuthDeniedPayloadCodec
{
    public const byte ReasonBadCredential = 0x01;
    public const byte ReasonExpiredCode = 0x02;
    public const byte ReasonDeviceLimit = 0x03;

    public static byte[] Encode(AuthDeniedPayload payload)
    {
        if (!IsKnownReason(payload.Reason))
            throw new ProtocolException("Unknown AUTH_DENIED reason.");

        var writer = new PayloadWriter();
        writer.WriteUInt8(payload.Reason);
        writer.WriteString(payload.Message);
        return writer.ToArray();
    }

    public static AuthDeniedPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var reason = reader.ReadUInt8();
        var message = reader.ReadString();
        reader.ExpectEnd();

        if (!IsKnownReason(reason))
            throw new ProtocolException("Unknown AUTH_DENIED reason.");

        return new AuthDeniedPayload(reason, message);
    }

    private static bool IsKnownReason(byte reason) =>
        reason == ReasonBadCredential ||
        reason == ReasonExpiredCode ||
        reason == ReasonDeviceLimit;
}
