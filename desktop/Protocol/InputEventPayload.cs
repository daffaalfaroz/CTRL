namespace CTRL.Desktop.Protocol;

public sealed record InputEventPayload(InputEvent Event);

public static class InputEventPayloadCodec
{
    public static byte[] Encode(InputEventPayload payload) =>
        InputEventCodec.Encode(payload.Event);

    public static InputEventPayload Decode(byte[] payload) =>
        new(InputEventCodec.Decode(payload));
}
