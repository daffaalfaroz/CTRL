using System.Text;

namespace CTRL.Desktop.Protocol;

public sealed class PayloadWriter
{
    private readonly List<byte> _bytes = new();

    public int Length => _bytes.Count;

    public void WriteUInt8(byte value) => _bytes.Add(value);

    public void WriteUInt16(ushort value)
    {
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)value);
    }

    public void WriteUInt32(uint value)
    {
        for (var shift = 24; shift >= 0; shift -= 8)
            _bytes.Add((byte)(value >> shift));
    }

    public void WriteUInt64(ulong value)
    {
        for (var shift = 56; shift >= 0; shift -= 8)
            _bytes.Add((byte)(value >> shift));
    }

    public void WriteFloat32(float value)
    {
        WriteUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
            _bytes.Add(b);
    }

    public void WriteString(string value, int minLength = 0, int maxLength = byte.MaxValue)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));
        var bytes = StrictUtf8.GetBytes(value);
        if (bytes.Length < minLength || bytes.Length > maxLength)
            throw new ProtocolException($"String must be {minLength}-{maxLength} UTF-8 bytes.");
        _bytes.Add((byte)bytes.Length);
        _bytes.AddRange(bytes);
    }

    public byte[] ToArray() => _bytes.ToArray();

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
