namespace CTRL.Desktop.Session;

/// <summary>
/// Per-direction outbound sequence counter (docs/protocol.md §7). Starts at 0
/// (or a seeded value for wrap tests); the session installs a fresh tracker at
/// the AUTH_OK boundary (§24.5) and values wrap modulo 2^16.
/// </summary>
public sealed class SequenceTracker
{
    private ushort _next;

    public SequenceTracker(ushort start = 0)
    {
        _next = start;
    }

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