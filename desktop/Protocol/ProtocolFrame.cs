namespace CTRL.Desktop.Protocol;

public readonly record struct ProtocolFrame(
    byte VersionMajor,
    byte VersionMinor,
    byte Flags,
    byte MessageType,
    ushort Sequence,
    ulong Timestamp,
    byte[] Payload)
{
    public const ushort Magic = 0x4354;
    public const int HeaderSize = 18;
    public const int MaxPayloadLength = ushort.MaxValue;

    public ushort PayloadLength => checked((ushort)Payload.Length);
}

public static class FrameCodec
{
    public const byte AckRequested = 0x01;
    public const byte Secure = 0x02;
    public const byte Compressed = 0x04;
    public const byte MustUnderstand = 0x08;
    private const byte AllowedFlags = AckRequested | MustUnderstand;

    /// <summary>
    /// Flags reserved for future versions (SECURE, COMPRESSED and the top four
    /// bits). Decode must NOT reject them (D2): the session layer answers with
    /// ERROR forbidden + close. Encode keeps rejecting them defensively.
    /// </summary>
    public const byte ReservedFlagsMask = 0xF6;

    public static byte[] Encode(ProtocolFrame frame)
    {
        Validate(frame);

        var bytes = new byte[ProtocolFrame.HeaderSize + frame.Payload.Length];
        bytes[0] = 0x43;
        bytes[1] = 0x54;
        bytes[2] = frame.VersionMajor;
        bytes[3] = frame.VersionMinor;
        bytes[4] = frame.Flags;
        bytes[5] = frame.MessageType;
        WriteUInt16BigEndian(bytes, 6, (ushort)frame.Payload.Length);
        WriteUInt16BigEndian(bytes, 8, frame.Sequence);
        WriteUInt64BigEndian(bytes, 10, frame.Timestamp);
        frame.Payload.CopyTo(bytes, ProtocolFrame.HeaderSize);
        return bytes;
    }

    public static ProtocolFrame Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < ProtocolFrame.HeaderSize)
            throw new ProtocolException("Frame header must be 18 bytes.");

        if (bytes[0] != 0x43 || bytes[1] != 0x54)
            throw new ProtocolException("Invalid frame magic.");

        var flags = bytes[4];

        var payloadLength = ReadUInt16BigEndian(bytes, 6);
        var expectedLength = ProtocolFrame.HeaderSize + payloadLength;
        if (bytes.Length != expectedLength)
            throw new ProtocolException("Frame length does not match PayloadLength.");

        var payload = bytes.Slice(ProtocolFrame.HeaderSize, payloadLength).ToArray();
        return new ProtocolFrame(
            bytes[2],
            bytes[3],
            flags,
            bytes[5],
            ReadUInt16BigEndian(bytes, 8),
            ReadUInt64BigEndian(bytes, 10),
            payload);
    }

    private static void Validate(ProtocolFrame frame)
    {
        if (frame.Payload is null)
            throw new ArgumentNullException(nameof(frame.Payload));
        if (frame.Payload.Length > ProtocolFrame.MaxPayloadLength)
            throw new ProtocolException("Payload exceeds 65535 bytes.");
        if ((frame.Flags & ~AllowedFlags) != 0)
            throw new ProtocolException("Reserved frame flags must be zero in v1.");
    }

    private static void WriteUInt16BigEndian(byte[] target, int offset, ushort value)
    {
        target[offset] = (byte)(value >> 8);
        target[offset + 1] = (byte)value;
    }

    private static void WriteUInt64BigEndian(byte[] target, int offset, ulong value)
    {
        for (var i = 0; i < 8; i++)
            target[offset + i] = (byte)(value >> (56 - (i * 8)));
    }

    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> source, int offset) =>
        (ushort)((source[offset] << 8) | source[offset + 1]);

    private static ulong ReadUInt64BigEndian(ReadOnlySpan<byte> source, int offset)
    {
        ulong value = 0;
        for (var i = 0; i < 8; i++)
            value = (value << 8) | source[offset + i];
        return value;
    }
}

public sealed class ProtocolException(string message) : Exception(message);
