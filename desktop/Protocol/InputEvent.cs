using System.Text;

namespace CTRL.Desktop.Protocol;

public sealed record InputEvent(
    string ControlId,
    byte Kind,
    byte Flags,
    byte? State = null,
    ushort? PressCount = null,
    float? Value = null,
    float? X = null,
    float? Y = null,
    byte? HatValue = null);

public static class InputEventCodec
{
    public const byte KindButton = 0x00;
    public const byte KindAxis = 0x01;
    public const byte KindStick = 0x02;
    public const byte KindTrigger = 0x03;
    public const byte KindHat = 0x04;

    public const byte FlagStateChanged = 0x01;
    public const byte FlagInitial = 0x02;

    public const byte StateUp = 0x00;
    public const byte StateDown = 0x01;

    public const byte MinControlIdLength = 1;
    public const byte MaxControlIdLength = 64;

    private const byte FlagsMask = FlagStateChanged | FlagInitial;

    public static byte[] Encode(InputEvent e)
    {
        var writer = new PayloadWriter();
        EncodeTo(e, writer);
        return writer.ToArray();
    }

    public static void EncodeTo(InputEvent e, PayloadWriter writer)
    {
        var controlIdBytes = StrictUtf8.GetBytes(e.ControlId);
        if (controlIdBytes.Length is < MinControlIdLength or > MaxControlIdLength)
            throw new ProtocolException("controlId must be 1..64 UTF-8 bytes.");
        if (!IsKnownKind(e.Kind))
            throw new ProtocolException("Unknown input event kind.");
        if ((e.Flags & ~FlagsMask) != 0)
            throw new ProtocolException("Invalid input event flags.");

        writer.WriteUInt8((byte)controlIdBytes.Length);
        writer.WriteBytes(controlIdBytes);
        writer.WriteUInt8(e.Kind);
        writer.WriteUInt8(e.Flags);

        switch (e.Kind)
        {
            case KindButton:
                if (e.State is null || (e.State != StateUp && e.State != StateDown))
                    throw new ProtocolException("Button event requires a valid state.");
                if (e.PressCount is null)
                    throw new ProtocolException("Button event requires a pressCount.");
                writer.WriteUInt8(e.State.Value);
                writer.WriteUInt16(e.PressCount.Value);
                break;
            case KindAxis:
            case KindTrigger:
                if (e.Value is null)
                    throw new ProtocolException("Axis/trigger event requires a value.");
                WriteRangeCheckedFloat(writer, e.Value.Value, 0f, 1f, "axis/trigger value");
                break;
            case KindStick:
                if (e.X is null || e.Y is null)
                    throw new ProtocolException("Stick event requires x and y.");
                WriteRangeCheckedFloat(writer, e.X.Value, -1f, 1f, "stick x");
                WriteRangeCheckedFloat(writer, e.Y.Value, -1f, 1f, "stick y");
                break;
            case KindHat:
                if (e.HatValue is null or > 8)
                    throw new ProtocolException("Hat event requires a value in 0..8.");
                writer.WriteUInt8(e.HatValue.Value);
                break;
        }
    }

    public static InputEvent Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var e = DecodeFrom(reader);
        reader.ExpectEnd();
        return e;
    }

    public static InputEvent DecodeFrom(PayloadReader reader)
    {
        var controlIdLength = reader.ReadUInt8();
        if (controlIdLength is < MinControlIdLength or > MaxControlIdLength)
            throw new ProtocolException("controlId must be 1..64 UTF-8 bytes.");
        var controlIdBytes = reader.ReadBytes(controlIdLength);
        var kind = reader.ReadUInt8();
        if (!IsKnownKind(kind))
            throw new ProtocolException("Unknown input event kind.");
        var flags = reader.ReadUInt8();
        if ((flags & ~FlagsMask) != 0)
            throw new ProtocolException("Invalid input event flags.");

        string controlId;
        try
        {
            controlId = StrictUtf8.GetString(controlIdBytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ProtocolException("Invalid UTF-8 controlId.");
        }

        byte? state = null;
        ushort? pressCount = null;
        float? value = null;
        float? x = null;
        float? y = null;
        byte? hatValue = null;

        switch (kind)
        {
            case KindButton:
                state = reader.ReadUInt8();
                if (state is not (StateUp or StateDown))
                    throw new ProtocolException("Invalid button state.");
                pressCount = reader.ReadUInt16();
                break;
            case KindAxis:
            case KindTrigger:
                value = ReadRangeCheckedFloat(reader, 0f, 1f, "axis/trigger value");
                break;
            case KindStick:
                x = ReadRangeCheckedFloat(reader, -1f, 1f, "stick x");
                y = ReadRangeCheckedFloat(reader, -1f, 1f, "stick y");
                break;
            case KindHat:
                hatValue = reader.ReadUInt8();
                if (hatValue > 8)
                    throw new ProtocolException("Invalid hat value.");
                break;
        }

        return new InputEvent(controlId, kind, flags, state, pressCount, value, x, y, hatValue);
    }

    private static void WriteRangeCheckedFloat(
        PayloadWriter writer, float v, float min, float max, string name)
    {
        if (float.IsNaN(v) || float.IsInfinity(v))
            throw new ProtocolException($"{name} must be finite.");
        if (v < min || v > max)
            throw new ProtocolException($"{name} is out of range.");
        writer.WriteFloat32(v);
    }

    private static float ReadRangeCheckedFloat(
        PayloadReader reader, float min, float max, string name)
    {
        var v = reader.ReadFloat32();
        if (float.IsNaN(v) || float.IsInfinity(v))
            throw new ProtocolException($"{name} must be finite.");
        if (v < min || v > max)
            throw new ProtocolException($"{name} is out of range.");
        return v;
    }

    private static bool IsKnownKind(byte kind) =>
        kind is KindButton or KindAxis or KindStick or KindTrigger or KindHat;

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
