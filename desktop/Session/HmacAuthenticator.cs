using System.Security.Cryptography;
using System.Text;
using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Session;

/// <summary>
/// Real §12 authenticator (M1.4.4). Wire contract is unchanged:
///
///   challengeResponse = HMAC-SHA256(sharedSecret, challenge)
///
/// where sharedSecret is the raw credential: the pairing code for
/// credentialType 0x02, or the persisted token for 0x01.
///
/// Token-at-rest design (reported decision): §12 requires BOTH that the server
/// verify HMAC-SHA256(token, challenge) AND store tokens hashed. A plain hash
/// cannot re-derive an HMAC over a random token, so tokens are DETERMINISTIC:
///
///   token(deviceId) = HMAC-SHA256(masterKey, "ctrl-token:" + deviceId)   (32 B)
///
/// The store keeps SHA-256(token) only — never plaintext. Verification
/// recomputes the token from the master key, checks the stored record still
/// exists and matches (so revocation works), then compares the presented
/// response in constant time.
/// </summary>
public sealed class HmacAuthenticator : IAuthenticator
{
    public const string TokenDerivationContext = "ctrl-token:";

    private readonly byte[] _masterKey;
    private readonly ITokenStore _tokenStore;
    private readonly PairingCodeService _pairing;
    private readonly AuthRateLimiter _limiter;

    public HmacAuthenticator(
        byte[] masterKey,
        ITokenStore? tokenStore = null,
        PairingCodeService? pairingService = null,
        AuthRateLimiter? rateLimiter = null)
    {
        if (masterKey.Length < 32)
            throw new ArgumentException("masterKey must be at least 32 bytes.", nameof(masterKey));
        _masterKey = masterKey;
        _tokenStore = tokenStore ?? new InMemoryTokenStore();
        _pairing = pairingService ?? new PairingCodeService();
        _limiter = rateLimiter ?? new AuthRateLimiter();
    }

    /// <summary>The deterministic persistent token for [deviceId].</summary>
    public byte[] DeriveToken(string deviceId) =>
        HmacSha256(_masterKey, Encoding.UTF8.GetBytes(TokenDerivationContext + deviceId));

    /// <summary>Hash form stored at rest for [deviceId] (SHA-256 of the token).</summary>
    public byte[] TokenHash(string deviceId) => Sha256(DeriveToken(deviceId));

    public AuthResult Authenticate(AuthPayload auth, byte[] challenge, string remoteAddress)
    {
        if (_limiter.IsLocked(remoteAddress, auth.DeviceId))
            return Denied(AuthDeniedPayloadCodec.ReasonBadCredential,
                "Too many failed attempts; try again later.");

        switch (auth.CredentialType)
        {
            case AuthPayloadCodec.CredentialTypeToken:
                return AuthenticateToken(auth, challenge, remoteAddress);
            case AuthPayloadCodec.CredentialTypePairingCode:
                return AuthenticatePairing(auth, challenge, remoteAddress);
            default:
                return Denied(AuthDeniedPayloadCodec.ReasonBadCredential,
                    "Unknown credential type.");
        }
    }

    private AuthResult AuthenticateToken(AuthPayload auth, byte[] challenge, string remoteAddress)
    {
        // Token bytes never travel the wire; the client proves possession via HMAC.
        var expectedToken = DeriveToken(auth.DeviceId);
        var expectedResponse = HmacSha256(expectedToken, challenge);

        if (!_tokenStore.TryGetHash(auth.DeviceId, out var storedHash) ||
            !ConstantTimeEquals(storedHash, Sha256(expectedToken)) ||
            !ConstantTimeEquals(expectedResponse, auth.ChallengeResponse))
        {
            return Fail(remoteAddress, auth.DeviceId,
                AuthDeniedPayloadCodec.ReasonBadCredential,
                "Invalid token or challenge response.");
        }

        _limiter.Reset(remoteAddress, auth.DeviceId);
        return Accepted(newToken: null); // §12 A: reconnect success has no newToken.
    }

    private AuthResult AuthenticatePairing(AuthPayload auth, byte[] challenge, string remoteAddress)
    {
        // Consistency first: the response must be derived from the presented code.
        var expectedResponse = HmacSha256(Encoding.UTF8.GetBytes(auth.Credential), challenge);
        if (!ConstantTimeEquals(expectedResponse, auth.ChallengeResponse))
        {
            return Fail(remoteAddress, auth.DeviceId,
                AuthDeniedPayloadCodec.ReasonBadCredential,
                "Invalid pairing code or challenge response.");
        }

        // Then consume atomically (single-use + TTL under one lock).
        var consumed = _pairing.Consume(auth.Credential, out _);
        switch (consumed)
        {
            case PairingConsumeResult.Consumed:
                break;
            case PairingConsumeResult.ExpiredCode:
                return Fail(remoteAddress, auth.DeviceId,
                    AuthDeniedPayloadCodec.ReasonExpiredCode,
                    "Pairing code expired.");
            default:
                return Fail(remoteAddress, auth.DeviceId,
                    AuthDeniedPayloadCodec.ReasonBadCredential,
                    "Unknown or already-used pairing code.");
        }

        var token = DeriveToken(auth.DeviceId);
        _tokenStore.StoreHash(auth.DeviceId, Sha256(token));
        _limiter.Reset(remoteAddress, auth.DeviceId);
        return Accepted(token); // §12 B: pairing success issues the persistent token.
    }

    private AuthResult Fail(string remoteAddress, string deviceId, byte reason, string message)
    {
        _limiter.RecordFailure(remoteAddress, deviceId);
        return Denied(reason, message);
    }

    private static AuthResult Accepted(byte[]? newToken) =>
        new(true, newToken, 0, string.Empty);

    private static AuthResult Denied(byte reason, string message) =>
        new(false, null, reason, message);

    internal static byte[] HmacSha256(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    internal static byte[] Sha256(byte[] data) => SHA256.HashData(data);

    internal static bool ConstantTimeEquals(byte[] a, byte[] b) =>
        CryptographicOperations.FixedTimeEquals(a, b);
}
