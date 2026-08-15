using System.Text;

namespace CTRL.Desktop.Protocol;

public sealed class PayloadReader
{
    private readonly byte[] _data;
    private int _offset;

    public PayloadReader(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public int Remaining => _data.Length - _offset;

    public byte ReadUInt8()
    {
        Require(1);
        return _data[_offset++];
    }

    public ushort ReadUInt16()
    {
        Require(2);
        var value = (ushort)((_data[_offset] << 8) | _data[_offset + 1]);
        _offset += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        Require(4);
        uint value = 0;
        for (var i = 0; i < 4; i++)
            value = (value << 8) | _data[_offset + i];
        _offset += 4;
        return value;
    }

    public ulong ReadUInt64()
    {
        Require(8);
        ulong value = 0;
        for (var i = 0; i < 8; i++)
            value = (value << 8) | _data[_offset + i];
        _offset += 8;
        return value;
    }

    public float ReadFloat32()
    {
        var bits = ReadUInt32();
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }

    public byte[] ReadBytes(int count)
    {
        if (count < 0)
            throw new ProtocolException("Invalid byte count.");
        Require(count);
        var result = new byte[count];
        Array.Copy(_data, _offset, result, 0, count);
        _offset += count;
        return result;
    }

    public string ReadString(int minLength = 0, int maxLength = byte.MaxValue)
    {
        var length = ReadUInt8();
        if (length < minLength || length > maxLength)
            throw new ProtocolException($"String must be {minLength}-{maxLength} UTF-8 bytes.");
        Require(length);
        var bytes = new byte[length];
        Array.Copy(_data, _offset, bytes, 0, length);
        _offset += length;
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ProtocolException("Invalid UTF-8 string.");
        }
    }

    public void ExpectEnd()
    {
        if (Remaining != 0)
            throw new ProtocolException("Payload contains extra bytes.");
    }

    private void Require(int count)
    {
        if (count > Remaining)
            throw new ProtocolException("Payload is truncated.");
    }

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
