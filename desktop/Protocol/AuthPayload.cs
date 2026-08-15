namespace CTRL.Desktop.Protocol;

public sealed record AuthPayload(
    byte CredentialType,
    string Credential,
    string DeviceId,
    byte[] ChallengeResponse);

public static class AuthPayloadCodec
{
    public const byte CredentialTypeToken = 0x01;
    public const byte CredentialTypePairingCode = 0x02;

    public static byte[] Encode(AuthPayload payload)
    {
        if (!IsKnownCredentialType(payload.CredentialType))
            throw new ProtocolException("Unknown credentialType.");
        if (payload.CredentialType == CredentialTypeToken &&
            !string.IsNullOrEmpty(payload.Credential))
            throw new ProtocolException("Token credential must be empty.");
        if (payload.ChallengeResponse is null || payload.ChallengeResponse.Length != 32)
            throw new ProtocolException("challengeResponse must be exactly 32 bytes.");

        var writer = new PayloadWriter();
        writer.WriteUInt8(payload.CredentialType);
        writer.WriteString(payload.Credential);
        writer.WriteString(payload.DeviceId, 1, 64);
        writer.WriteBytes(payload.ChallengeResponse);
        return writer.ToArray();
    }

    public static AuthPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var credentialType = reader.ReadUInt8();
        var credential = reader.ReadString();
        var deviceId = reader.ReadString(1, 64);
        var challengeResponse = reader.ReadBytes(32);
        reader.ExpectEnd();

        if (!IsKnownCredentialType(credentialType))
            throw new ProtocolException("Unknown credentialType.");
        if (credentialType == CredentialTypeToken && credential.Length != 0)
            throw new ProtocolException("Token credential must have length 0.");

        return new AuthPayload(credentialType, credential, deviceId, challengeResponse);
    }

    private static bool IsKnownCredentialType(byte credentialType) =>
        credentialType == CredentialTypeToken ||
        credentialType == CredentialTypePairingCode;
}
