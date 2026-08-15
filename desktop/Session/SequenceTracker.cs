namespace CTRL.Desktop.Session;

/// <summary>
/// Continuous per-direction sequence counter (D1: no reset after AUTH_OK,
/// per docs/protocol.md §19). Wraps modulo 2^16.
/// </summary>
public sealed class SequenceTracker
{
    private ushort _next;

    public ushort Next()
    {
        var value = _next;
        _next++;
        return value;
    }

    public ushort Current => _next;
}

/// <summary>
/// Tracks the last received sequence in one direction and validates that the
/// next one is monotonically increasing modulo 2^16 (delta 1..0x7FFF). Failures
/// are reported by the session but do not tear down the connection (§19).
/// </summary>
public sealed class InboundSequenceTracker
{
    private bool _hasLast;
    private ushort _last;

    public bool IsMonotonic(ushort sequence)
    {
        if (!_hasLast)
        {
            _last = sequence;
            _hasLast = true;
            return true;
        }

        var delta = (ushort)(sequence - _last);
        _last = sequence;
        return delta != 0 && delta <= 0x7FFF;
    }
}