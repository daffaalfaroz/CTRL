using System.Security.Cryptography;

namespace CTRL.Desktop.Session;

public enum PairingConsumeResult
{
    Consumed,
    UnknownCode,
    ExpiredCode,
}

/// <summary>
/// Issues and consumes single-use pairing codes (docs/protocol.md §12 B):
/// 6-digit codes, TTL 300 s, consumed atomically so a code can never be used
/// by two authentications. The clock is injected for deterministic tests.
/// </summary>
public sealed class PairingCodeService
{
    public const ulong DefaultTtlMs = 300_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, ulong> _expiresAtMs = new();
    private readonly Func<ulong> _nowMs;
    private readonly Func<string> _codeFactory;
    private readonly ulong _ttlMs;

    public PairingCodeService(
        Func<ulong>? nowMs = null,
        Func<string>? codeFactory = null,
        ulong ttlMs = DefaultTtlMs)
    {
        _nowMs = nowMs ?? DefaultNow;
        _codeFactory = codeFactory ?? GenerateSixDigitCode;
        _ttlMs = ttlMs;
    }

    /// <summary>Issues a fresh single-use pairing code valid for the TTL.</summary>
    public string Issue()
    {
        var code = _codeFactory();
        IssueExplicit(code);
        return code;
    }

    /// <summary>Registers a specific code (tests / harness pin codes).</summary>
    public void IssueExplicit(string code) => Register(code, _nowMs() + _ttlMs);

    private void Register(string code, ulong expiresAtMs)
    {
        if (string.IsNullOrEmpty(code))
            throw new ArgumentException("Pairing code must not be empty.", nameof(code));
        lock (_gate)
        {
            _expiresAtMs[code] = expiresAtMs;
        }
    }

    /// <summary>
    /// Atomically consumes [code]. Returns <see cref="PairingConsumeResult.Consumed"/>
    /// exactly once per issued code; an expired code is removed and reported as
    /// <see cref="PairingConsumeResult.ExpiredCode"/>. All checks run inside one
    /// lock, so concurrent reuse can never succeed twice.
    /// </summary>
    public PairingConsumeResult Consume(string code, out ulong expiredAtMs)
    {
        expiredAtMs = 0;
        lock (_gate)
        {
            var now = _nowMs();
            if (!_expiresAtMs.TryGetValue(code, out var expiresAt))
                return PairingConsumeResult.UnknownCode;

            _expiresAtMs.Remove(code);
            if (expiresAt <= now)
            {
                expiredAtMs = expiresAt;
                return PairingConsumeResult.ExpiredCode;
            }
            return PairingConsumeResult.Consumed;
        }
    }

    private static ulong DefaultNow() => (ulong)Environment.TickCount64;

    private static string GenerateSixDigitCode()
    {
        Span<byte> bytes = stackalloc byte[2];
        uint value;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = ((uint)bytes[0] << 8) | bytes[1];
        } while (value >= 1_000_000); // rejection sampling keeps digits uniform

        return value.ToString("D6");
    }
}
