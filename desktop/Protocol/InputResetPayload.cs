namespace CTRL.Desktop.Protocol;

public sealed record InputResetPayload(byte Reason);

public static class InputResetPayloadCodec
{
    public const byte ReasonStateReset = 0x00;
    public const byte ReasonProfileSwitch = 0x01;
    public const byte ReasonMaintenance = 0x02;

    public static byte[] Encode(InputResetPayload payload)
    {
        if (!IsKnownReason(payload.Reason))
            throw new ProtocolException("Unknown INPUT_RESET reason.");
        var writer = new PayloadWriter();
        writer.WriteUInt8(payload.Reason);
        return writer.ToArray();
    }

    public static InputResetPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var reason = reader.ReadUInt8();
        reader.ExpectEnd();
        if (!IsKnownReason(reason))
            throw new ProtocolException("Unknown INPUT_RESET reason.");
        return new InputResetPayload(reason);
    }

    private static bool IsKnownReason(byte reason) =>
        reason is ReasonStateReset or ReasonProfileSwitch or ReasonMaintenance;
}
