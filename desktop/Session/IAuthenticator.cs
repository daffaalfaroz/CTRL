using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Session;

public sealed record AuthResult(
    bool Accepted,
    byte[]? NewToken,
    byte DeniedReason,
    string DeniedMessage);

/// <summary>
/// Verifies an AUTH payload against the server-issued challenge. M1.4.4
/// implements the §12 contract: challengeResponse = HMAC-SHA256(sharedSecret,
/// challenge) where sharedSecret is the raw credential (persisted token for
/// 0x01, pairing code for 0x02). <paramref name="remoteAddress"/> scopes the
/// auth-failure lockout (5 failures → 30 s per IP/deviceId).
/// </summary>
public interface IAuthenticator
{
    AuthResult Authenticate(AuthPayload auth, byte[] challenge, string remoteAddress);
}