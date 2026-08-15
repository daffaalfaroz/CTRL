using System.Text;

namespace CTRL.Desktop.Protocol;

public sealed record ErrorPayload(byte Code, byte Severity, string Message);

public static class ErrorPayloadCodec
{
    public const byte CodeProtocolVersionMismatch = 0x01;
    public const byte CodeAuthFailed = 0x02;
    public const byte CodeNotAuthenticated = 0x03;
    public const byte CodeDeviceLimit = 0x04;
    public const byte CodePayloadTooLarge = 0x05;
    public const byte CodeInvalidMessage = 0x06;
    public const byte CodeUnsupportedMessage = 0x07;
    public const byte CodeForbidden = 0x08;
    public const byte CodeServerShutdown = 0x09;
    public const byte CodeInternal = 0xFF;

    public const byte SeverityInfo = 0x00;
    public const byte SeverityWarn = 0x01;
    public const byte SeverityFatal = 0x02;

    public const int MaxMessageLength = 1024;

    public static byte[] Encode(ErrorPayload payload)
    {
        if (!IsKnownCode(payload.Code))
            throw new ProtocolException("Unknown ERROR code.");
        if (!IsKnownSeverity(payload.Severity))
            throw new ProtocolException("Unknown ERROR severity.");

        var messageBytes = StrictUtf8.GetBytes(payload.Message);
        if (messageBytes.Length > MaxMessageLength)
            throw new ProtocolException("ERROR message exceeds 1024 bytes.");

        var writer = new PayloadWriter();
        writer.WriteUInt8(payload.Code);
        writer.WriteUInt8(payload.Severity);
        writer.WriteUInt16((ushort)messageBytes.Length);
        writer.WriteBytes(messageBytes);
        return writer.ToArray();
    }

    public static ErrorPayload Decode(byte[] payload)
    {
        var reader = new PayloadReader(payload);
        var code = reader.ReadUInt8();
        var severity = reader.ReadUInt8();
        var messageLength = reader.ReadUInt16();
        if (messageLength > MaxMessageLength)
            throw new ProtocolException("ERROR message exceeds 1024 bytes.");
        var messageBytes = reader.ReadBytes(messageLength);
        reader.ExpectEnd();

        if (!IsKnownCode(code))
            throw new ProtocolException("Unknown ERROR code.");
        if (!IsKnownSeverity(severity))
            throw new ProtocolException("Unknown ERROR severity.");

        string message;
        try
        {
            message = StrictUtf8.GetString(messageBytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ProtocolException("Invalid UTF-8 message.");
        }

        return new ErrorPayload(code, severity, message);
    }

    private static bool IsKnownCode(byte code) =>
        code is CodeProtocolVersionMismatch or CodeAuthFailed or
            CodeNotAuthenticated or CodeDeviceLimit or CodePayloadTooLarge or
            CodeInvalidMessage or CodeUnsupportedMessage or CodeForbidden or
            CodeServerShutdown or CodeInternal;

    private static bool IsKnownSeverity(byte severity) =>
        severity is SeverityInfo or SeverityWarn or SeverityFatal;

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
