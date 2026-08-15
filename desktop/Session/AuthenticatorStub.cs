using System.Security.Cryptography;
using System.Text;
using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Session;

/// <summary>
/// D4 stub authenticator with NO real crypto. Two credential modes:
///   - Token: challengeResponse must echo the challenge and the deviceId must
///     be a known trusted device.
///   - PairingCode: challengeResponse must echo the challenge and the
///     credential must equal the configured pairing code (issued token is
///     deterministic so tests can pin it).
/// This is intentionally NOT HMAC; the next milestone replaces the echo with
/// HMAC-SHA256(token|pairingCode, challenge).
/// </summary>
public sealed class AuthenticatorStub : IAuthenticator
{
    private readonly HashSet<string> _trustedDevices;
    private readonly string _pairingCode;

    public AuthenticatorStub(IEnumerable<string>? trustedDevices = null, string pairingCode = "123456")
    {
        _trustedDevices = new HashSet<string>(trustedDevices ?? Array.Empty<string>());
        _pairingCode = pairingCode;
    }

    public AuthResult Authenticate(AuthPayload auth, byte[] challenge)
    {
        var responseEchoesChallenge = auth.ChallengeResponse.SequenceEqual(challenge);

        switch (auth.CredentialType)
        {
            case AuthPayloadCodec.CredentialTypeToken:
                if (!responseEchoesChallenge || !_trustedDevices.Contains(auth.DeviceId))
                    return Denied();
                return Accepted(newToken: null);

            case AuthPayloadCodec.CredentialTypePairingCode:
                if (!responseEchoesChallenge || auth.Credential != _pairingCode)
                    return Denied();
                return Accepted(newToken: DeriveToken(auth.DeviceId));

            default:
                return Denied();
        }
    }

    private static AuthResult Accepted(byte[]? newToken) =>
        new(true, newToken, 0, string.Empty);

    private static AuthResult Denied() =>
        new(false, null, AuthDeniedPayloadCodec.ReasonBadCredential, "Bad credential or challenge response.");

    /// <summary>Deterministic 16-byte stub token derived from the deviceId
    /// (no crypto, mirrors the no-HMAC D4 decision).</summary>
    private static byte[] DeriveToken(string deviceId)
    {
        var nameBytes = Encoding.UTF8.GetBytes($"stub:{deviceId}");
        var bytes = SHA256.HashData(nameBytes);
        var token = new byte[16];
        Array.Copy(bytes, token, token.Length);
        return token;
    }
}