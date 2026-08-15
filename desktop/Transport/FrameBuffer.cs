using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Transport;

/// <summary>
/// Incremental frame reassembly buffer for the CTRL protocol framing
/// (fixed 18-byte header + PayloadLength bytes payload, no delimiters).
/// Only performs reassembly; semantic validation stays in <see cref="FrameCodec"/>.
/// </summary>
public sealed class FrameBuffer
{
    public const int MaxFrameSize =
        ProtocolFrame.HeaderSize + ProtocolFrame.MaxPayloadLength;

    private byte[] _buffer = new byte[MaxFrameSize];
    private int _start;
    private int _end;

    /// <summary>True when bytes remain buffered that have not been consumed yet.</summary>
    public bool HasBufferedData => _start < _end;

    /// <summary>
    /// Appends bytes received from the socket. Grows the backing buffer only
    /// as data actually arrives; never pre-allocates based on network length.
    /// </summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(_end));
        _end += data.Length;
        CompactIfNeeded();
    }

    /// <summary>
    /// Extracts one complete frame if enough bytes are buffered. Returns true
    /// and sets <paramref name="frame"/> to the full frame bytes (header +
    /// payload) when available. May extract several frames over repeated calls
    /// when a single TCP read carried multiple frames.
    /// </summary>
    public bool TryReadFrame(out byte[] frame)
    {
        frame = null!;
        var available = _end - _start;
        if (available < ProtocolFrame.HeaderSize)
            return false;

        var payloadLength = (_buffer[_start + 6] << 8) | _buffer[_start + 7];
        var frameSize = ProtocolFrame.HeaderSize + payloadLength;
        if (frameSize > MaxFrameSize)
            throw new ProtocolException("Frame length exceeds maximum allowed size.");

        if (available < frameSize)
            return false;

        frame = _buffer.AsSpan(_start, frameSize).ToArray();
        _start += frameSize;
        CompactIfNeeded();
        return true;
    }

    private void EnsureCapacity(int extra)
    {
        var required = (_end - _start) + extra;
        if (required <= _buffer.Length)
            return;

        if (_start > 0)
        {
            Compact();
            required = (_end - _start) + extra;
            if (required <= _buffer.Length)
                return;
        }

        var newSize = Math.Max(_buffer.Length * 2, required);
        Array.Resize(ref _buffer, newSize);
    }

    private void Compact()
    {
        var count = _end - _start;
        if (count == 0)
        {
            _start = 0;
            _end = 0;
            return;
        }
        Buffer.BlockCopy(_buffer, _start, _buffer, 0, count);
        _start = 0;
        _end = count;
    }

    private void CompactIfNeeded()
    {
        if (_start >= 4096 && _start >= (_end - _start))
            Compact();
    }
}
