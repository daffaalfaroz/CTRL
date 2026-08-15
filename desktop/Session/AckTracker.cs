namespace CTRL.Desktop.Session;

/// <summary>
/// Tracks outbound messages that requested an ACK and, on timeout, retries
/// exactly once (docs/protocol.md §7). Runs on an injected clock so it stays
/// fully deterministic in tests — no real timers in this milestone.
/// </summary>
public sealed class AckTracker
{
    private readonly Func<ulong> _nowMs;
    private readonly ulong _timeoutMs;
    private readonly Dictionary<ushort, PendingAck> _pending = new();

    private sealed class PendingAck
    {
        public ulong SentAtMs;
        public int Attempts;
    }

    public AckTracker(Func<ulong> nowMs, ulong timeoutMs)
    {
        _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
        _timeoutMs = timeoutMs;
    }

    public int PendingCount => _pending.Count;

    public void Track(ushort sequence)
    {
        _pending[sequence] = new PendingAck { SentAtMs = _nowMs(), Attempts = 0 };
    }

    public void Acknowledge(ushort sequence) => _pending.Remove(sequence);

    public bool IsPending(ushort sequence) => _pending.ContainsKey(sequence);

    /// <summary>
    /// Sequences whose first deadline passed and still await an ACK. Each call
    /// returns each sequence at most once (it is marked as retried).
    /// </summary>
    public IReadOnlyList<ushort> RetryExpired()
    {
        var now = _nowMs();
        var expired = new List<ushort>();
        foreach (var pair in _pending)
        {
            if (pair.Value.Attempts == 0 && now - pair.Value.SentAtMs >= _timeoutMs)
            {
                pair.Value.Attempts = 1;
                pair.Value.SentAtMs = now;
                expired.Add(pair.Key);
            }
        }
        return expired;
    }

    /// <summary>Sequences still unacknowledged after the retry deadline (failed).</summary>
    public IReadOnlyList<ushort> Failed()
    {
        var now = _nowMs();
        var failed = new List<ushort>();
        foreach (var pair in _pending)
        {
            if (pair.Value.Attempts >= 1 && now - pair.Value.SentAtMs >= _timeoutMs)
                failed.Add(pair.Key);
        }
        return failed;
    }
}