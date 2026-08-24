namespace CTRL.Desktop.Session;

/// <summary>
/// Auth-failure lockout (docs/protocol.md §12: "5 percobaan gagal → kunci
/// 30 detik, per IP/deviceId"). Counters are strictly per (IP, deviceId) —
/// never global. Semantics (minimal and reported in the M1.4.4 notes):
///   - failures accumulate until a successful authentication resets them;
///   - the 5th failure locks the key until now + LockoutMs;
///   - attempts during a lock are denied and do NOT extend it;
///   - after expiry a NEW failure locks again immediately.
/// The clock is injected for deterministic tests.
/// </summary>
public sealed class AuthRateLimiter
{
    public const int DefaultMaxFailures = 5;
    public const ulong DefaultLockoutMs = 30_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly int _maxFailures;
    private readonly ulong _lockoutMs;
    private readonly Func<ulong> _nowMs;

    public AuthRateLimiter(
        int maxFailures = DefaultMaxFailures,
        ulong lockoutMs = DefaultLockoutMs,
        Func<ulong>? nowMs = null)
    {
        _maxFailures = maxFailures;
        _lockoutMs = lockoutMs;
        _nowMs = nowMs ?? DefaultNow;
    }

    /// <summary>True while the key is locked out.</summary>
    public bool IsLocked(string remoteAddress, string deviceId)
    {
        var key = BuildKey(remoteAddress, deviceId);
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return false;
            if (_nowMs() < entry.LockedUntilMs)
                return true;
            return false;
        }
    }

    /// <summary>Records one failed authentication for the key.</summary>
    public void RecordFailure(string remoteAddress, string deviceId)
    {
        var key = BuildKey(remoteAddress, deviceId);
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
                _entries[key] = entry = new Entry();
            entry.Failures++;
            if (entry.Failures >= _maxFailures)
                entry.LockedUntilMs = _nowMs() + _lockoutMs;
        }
    }

    /// <summary>Clears failure state after a successful authentication.</summary>
    public void Reset(string remoteAddress, string deviceId)
    {
        var key = BuildKey(remoteAddress, deviceId);
        lock (_gate)
        {
            _entries.Remove(key);
        }
    }

    internal int FailureCount(string remoteAddress, string deviceId)
    {
        var key = BuildKey(remoteAddress, deviceId);
        lock (_gate)
        {
            return _entries.TryGetValue(key, out var entry) ? entry.Failures : 0;
        }
    }

    private static string BuildKey(string remoteAddress, string deviceId) =>
        $"{remoteAddress}|{deviceId}";

    private static ulong DefaultNow() => (ulong)Environment.TickCount64;

    private sealed class Entry
    {
        public int Failures;
        public ulong LockedUntilMs;
    }
}
