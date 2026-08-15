namespace CTRL.Desktop.Protocol;

public sealed record DisconnectPayload(byte Reason);

public static class DisconnectPayloadCodec
{
    public const byte ReasonNormal = 0x00;
    public const byte ReasonAppClosing = 0x01;
    public const byte ReasonServerRestart = 0x02;
    public const byte ReasonIdleTimeout = 0x03;
    public const byte ReasonSecurity = 0x04;
    public const byte ReasonProtocolViolation = 0x05;

    public static byte[] Encode(DisconnectPayload payload)
    {
        if (!IsKnownReason(payload.Reason))
            throw new ProtocolException("Unknown DISCONNECT reason.");

        var writer = new PayloadWriter();
        writer.WriteUInt8(payload.Reason);
        return writer.ToArray();
    }

    public static DisconnectPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var reason = reader.ReadUInt8();
        reader.ExpectEnd();

        if (!IsKnownReason(reason))
            throw new ProtocolException("Unknown DISCONNECT reason.");

        return new DisconnectPayload(reason);
    }

    private static bool IsKnownReason(byte reason) => reason <= ReasonProtocolViolation;
}
