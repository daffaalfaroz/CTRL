using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Session;

public sealed record AuthResult(
    bool Accepted,
    byte[]? NewToken,
    byte DeniedReason,
    string DeniedMessage);

/// <summary>
/// Verifies an AUTH payload against the server-issued challenge. The real
/// implementation (later milestone) will use HMAC-SHA256; D4 keeps a stub.
/// </summary>
public interface IAuthenticator
{
    AuthResult Authenticate(AuthPayload auth, byte[] challenge);
}