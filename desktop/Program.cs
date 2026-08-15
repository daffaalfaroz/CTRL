using CTRL.Desktop.Protocol;

RunFrameCodecSmokeTests();
RunPayloadPrimitiveSmokeTests();
RunHelloCodecSmokeTests();
RunWelcomeCodecSmokeTests();
RunAuthCodecSmokeTests();
RunAuthOkCodecSmokeTests();
RunAuthDeniedCodecSmokeTests();
RunErrorCodecSmokeTests();
RunDisconnectCodecSmokeTests();
RunHeartbeatCodecSmokeTests();
RunPongCodecSmokeTests();
RunAckCodecSmokeTests();
RunInputEventCodecSmokeTests();
RunInputSnapshotCodecSmokeTests();
RunInputResetCodecSmokeTests();

Console.WriteLine("M1.1 + M1.2.1 + M1.2.2 + M1.2.3 protocol smoke tests passed.");

static void RunFrameCodecSmokeTests()
{
    var frame = new ProtocolFrame(
        VersionMajor: 1,
        VersionMinor: 0,
        Flags: FrameCodec.AckRequested,
        MessageType: 0x01,
        Sequence: 0x1234,
        Timestamp: 0x0102030405060708UL,
        Payload: [0xDE, 0xAD, 0xBE, 0xEF]);

    var encoded = FrameCodec.Encode(frame);
    if (encoded.Length != ProtocolFrame.HeaderSize + 4)
        throw new Exception("Unexpected encoded frame length.");

    if (encoded[0] != 0x43 || encoded[1] != 0x54 || encoded[6] != 0x00 || encoded[7] != 0x04)
        throw new Exception("Header encoding is not big-endian as required.");

    var decoded = FrameCodec.Decode(encoded);
    if (decoded.VersionMajor != frame.VersionMajor ||
        decoded.VersionMinor != frame.VersionMinor ||
        decoded.Flags != frame.Flags ||
        decoded.MessageType != frame.MessageType ||
        decoded.Sequence != frame.Sequence ||
        decoded.Timestamp != frame.Timestamp ||
        !decoded.Payload.SequenceEqual(frame.Payload))
        throw new Exception("Frame round-trip failed.");

    ExpectProtocolException(() => FrameCodec.Decode(encoded[..^1]), "truncated frame");
    ExpectProtocolException(() => FrameCodec.Decode([0x00, 0x00, ..encoded[2..]]), "invalid magic");
    ExpectProtocolException(() => FrameCodec.Decode([..encoded[..4], 0x02, ..encoded[5..]]), "reserved flag");
}

static void RunPayloadPrimitiveSmokeTests()
{
    var writer = new PayloadWriter();
    writer.WriteUInt8(0xAB);
    writer.WriteUInt16(0x1234);
    writer.WriteUInt32(0x01020304);
    writer.WriteUInt64(0x0102030405060708UL);
    writer.WriteFloat32(0.5f);
    writer.WriteString("hi");
    writer.WriteString("€");

    var reader = new PayloadReader(writer.ToArray());
    if (reader.ReadUInt8() != 0xAB) throw new Exception("uint8 round-trip failed.");
    if (reader.ReadUInt16() != 0x1234) throw new Exception("uint16 round-trip failed.");
    if (reader.ReadUInt32() != 0x01020304) throw new Exception("uint32 round-trip failed.");
    if (reader.ReadUInt64() != 0x0102030405060708UL) throw new Exception("uint64 round-trip failed.");
    if (reader.ReadFloat32() != 0.5f) throw new Exception("float32 round-trip failed.");
    if (reader.ReadString() != "hi") throw new Exception("string round-trip failed.");
    if (reader.ReadString() != "€") throw new Exception("multi-byte string round-trip failed.");
    reader.ExpectEnd();

    var stringBytes = new PayloadWriter();
    stringBytes.WriteString("€");
    var stringPayload = stringBytes.ToArray();
    if (stringPayload[0] != 3)
        throw new Exception("String prefix must be the UTF-8 byte length, not char count.");

    ExpectProtocolException(() => new PayloadReader([0x01]).ReadUInt16(), "truncated uint16");
    ExpectProtocolException(() => new PayloadReader([0x00]).ReadUInt32(), "truncated uint32");
    ExpectProtocolException(() => new PayloadReader([0x00, 0x00, 0x00, 0x00]).ReadUInt64(), "truncated uint64");
    ExpectProtocolException(() => new PayloadReader([0x00]).ReadBytes(2), "truncated bytes");
    ExpectProtocolException(() => new PayloadReader([0x02, 0x61]).ReadString(), "length prefix past end");
    ExpectProtocolException(() => new PayloadReader([0x01, 0xFF]).ReadString(), "invalid UTF-8");
    ExpectProtocolException(() => new PayloadReader([0x01, 0x41, 0x42]).ExpectEnd(), "extra bytes");
}

static void RunHelloCodecSmokeTests()
{
    var hello = new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007);
    var encoded = HelloPayloadCodec.Encode(hello);

    byte[] expected =
    [
        0x09,
        0x63, 0x74, 0x72, 0x6C, 0x2D, 0x34, 0x32, 0x61, 0x38,
        0x05,
        0x30, 0x2E, 0x31, 0x2E, 0x30,
        0x01, 0x00,
        0x00, 0x00, 0x00, 0x07
    ];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("HELLO wire bytes do not match docs/protocol.md 19.1.");

    var decoded = HelloPayloadCodec.Decode(encoded);
    if (decoded != hello)
        throw new Exception("HELLO round-trip failed.");

    ExpectProtocolException(() => HelloPayloadCodec.Decode(encoded[..^1]), "truncated hello");
    ExpectProtocolException(() => HelloPayloadCodec.Decode([..encoded, 0x00]), "hello extra bytes");
    ExpectProtocolException(() => HelloPayloadCodec.Decode([0x01, 0xFF, 0x00, 0x00, 0x00, 0x00]), "hello invalid utf8");
    ExpectProtocolException(() => HelloPayloadCodec.Decode([0x03, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00]), "hello bad length prefix");
}

static void RunWelcomeCodecSmokeTests()
{
    var welcome = new WelcomePayload(
        "CTRL-PC",
        1,
        0,
        1,
        [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F],
        true,
        Enumerable.Range(0x10, 32).Select(i => (byte)i).ToArray());
    var encoded = WelcomePayloadCodec.Encode(welcome);

    byte[] expected =
    [
        0x07,
        0x43, 0x54, 0x52, 0x4C, 0x2D, 0x50, 0x43,
        0x01, 0x00, 0x01,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x01,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
        0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
        0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F
    ];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("WELCOME wire bytes do not match docs/protocol.md 19.2.");

    var decoded = WelcomePayloadCodec.Decode(encoded);
    if (decoded.ServerName != welcome.ServerName ||
        decoded.EffectiveMajor != welcome.EffectiveMajor ||
        decoded.EffectiveMinor != welcome.EffectiveMinor ||
        decoded.MinSupportedMajor != welcome.MinSupportedMajor ||
        !decoded.SessionId.SequenceEqual(welcome.SessionId) ||
        decoded.AuthRequired != welcome.AuthRequired ||
        !decoded.Challenge.SequenceEqual(welcome.Challenge))
        throw new Exception("WELCOME round-trip failed.");

    ExpectProtocolException(() => WelcomePayloadCodec.Decode(encoded[..^1]), "truncated welcome");
    ExpectProtocolException(() => WelcomePayloadCodec.Decode([..encoded, 0x00]), "welcome extra bytes");
    ExpectProtocolException(() => WelcomePayloadCodec.Encode(welcome with { SessionId = new byte[15] }), "welcome short sessionId (encode)");
    ExpectProtocolException(() => WelcomePayloadCodec.Encode(welcome with { Challenge = new byte[31] }), "welcome short challenge (encode)");

    var tamperedSession = new byte[encoded.Length - 1];
    Array.Copy(encoded, 0, tamperedSession, 0, 26);
    Array.Copy(encoded, 27, tamperedSession, 26, encoded.Length - 27);
    ExpectProtocolException(() => WelcomePayloadCodec.Decode(tamperedSession), "welcome short sessionId (decode)");

    var badAuth = (byte[])encoded.Clone();
    badAuth[27] = 0x02;
    ExpectProtocolException(() => WelcomePayloadCodec.Decode(badAuth), "welcome invalid authRequired");
}

static void RunAuthCodecSmokeTests()
{
    var auth = new AuthPayload(
        0x02,
        "123456",
        "ctrl-42a8",
        Enumerable.Range(0x50, 32).Select(i => (byte)i).ToArray());
    var encoded = AuthPayloadCodec.Encode(auth);

    byte[] expected =
    [
        0x02, 0x06,
        0x31, 0x32, 0x33, 0x34, 0x35, 0x36,
        0x09,
        0x63, 0x74, 0x72, 0x6C, 0x2D, 0x34, 0x32, 0x61, 0x38,
        0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
        0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
        0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F
    ];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("AUTH wire bytes do not match docs/protocol.md 19.3.");

    var decoded = AuthPayloadCodec.Decode(encoded);
    if (decoded.CredentialType != auth.CredentialType ||
        decoded.Credential != auth.Credential ||
        decoded.DeviceId != auth.DeviceId ||
        !decoded.ChallengeResponse.SequenceEqual(auth.ChallengeResponse))
        throw new Exception("AUTH round-trip failed.");

    var token = new AuthPayload(0x01, "", "ctrl-42a8", new byte[32]);
    var tokenBytes = AuthPayloadCodec.Encode(token);
    if (tokenBytes[0] != 0x01 || tokenBytes[1] != 0x00)
        throw new Exception("AUTH token must encode an empty credential.");
    var tokenDecoded = AuthPayloadCodec.Decode(tokenBytes);
    if (tokenDecoded.CredentialType != 0x01 || tokenDecoded.Credential.Length != 0)
        throw new Exception("AUTH token round-trip failed.");

    var boundary = new AuthPayload(0x02, new string('a', 255), "d", new byte[32]);
    var boundaryEncoded = AuthPayloadCodec.Encode(boundary);
    var boundaryDecoded = AuthPayloadCodec.Decode(boundaryEncoded);
    if (boundaryDecoded.Credential.Length != 255)
        throw new Exception("AUTH 255-byte credential boundary failed.");

    ExpectProtocolException(() => AuthPayloadCodec.Encode(new AuthPayload(0x03, "", "d", new byte[32])), "auth unknown credentialType (encode)");
    ExpectProtocolException(() => AuthPayloadCodec.Encode(new AuthPayload(0x01, "x", "d", new byte[32])), "auth token with non-empty credential (encode)");
    ExpectProtocolException(() => AuthPayloadCodec.Encode(new AuthPayload(0x02, "", "d", new byte[31])), "auth short challengeResponse (encode)");
    ExpectProtocolException(() => AuthPayloadCodec.Encode(new AuthPayload(0x02, new string('a', 256), "d", new byte[32])), "auth credential over 255 bytes (encode)");
    ExpectProtocolException(() => AuthPayloadCodec.Decode(encoded[..^1]), "auth truncated");
    ExpectProtocolException(() => AuthPayloadCodec.Decode([..encoded, 0x00]), "auth extra bytes");

    var badType = (byte[])encoded.Clone();
    badType[0] = 0x03;
    ExpectProtocolException(() => AuthPayloadCodec.Decode(badType), "auth unknown credentialType (decode)");

    var badTokenCredential = new byte[] { 0x01, 0x01, 0x41, 0x00 }.Concat(new byte[32]).ToArray();
    ExpectProtocolException(() => AuthPayloadCodec.Decode(badTokenCredential), "auth token credential length must be 0 (decode)");

    var badUtf8 = new byte[] { 0x02, 0x00, 0x01, 0xFF }.Concat(new byte[32]).ToArray();
    ExpectProtocolException(() => AuthPayloadCodec.Decode(badUtf8), "auth invalid utf8 deviceId");
}

static void RunAuthOkCodecSmokeTests()
{
    var ok = new AuthOkPayload(
        0x00,
        [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F],
        0x00000007,
        "a1b2c3d4e5f60718");
    var encoded = AuthOkPayloadCodec.Encode(ok);

    byte[] expected =
    [
        0x00,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x00, 0x00, 0x00, 0x07,
        0x10,
        0x61, 0x31, 0x62, 0x32, 0x63, 0x33, 0x64, 0x34, 0x65, 0x35,
        0x66, 0x36, 0x30, 0x37, 0x31, 0x38
    ];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("AUTH_OK wire bytes do not match docs/protocol.md 19.4.");

    var decoded = AuthOkPayloadCodec.Decode(encoded);
    if (decoded.Result != ok.Result ||
        !decoded.SessionId.SequenceEqual(ok.SessionId) ||
        decoded.ServerCapabilities != ok.ServerCapabilities ||
        decoded.NewToken != ok.NewToken)
        throw new Exception("AUTH_OK round-trip failed.");

    var noToken = new AuthOkPayload(0x00, new byte[16], 0x00000007, "");
    var noTokenBytes = AuthOkPayloadCodec.Encode(noToken);
    if (noTokenBytes[^1] != 0x00)
        throw new Exception("AUTH_OK empty newToken must encode length 0.");
    var noTokenDecoded = AuthOkPayloadCodec.Decode(noTokenBytes);
    if (noTokenDecoded.NewToken.Length != 0 ||
        !noTokenDecoded.SessionId.SequenceEqual(noToken.SessionId) ||
        noTokenDecoded.ServerCapabilities != noToken.ServerCapabilities)
        throw new Exception("AUTH_OK empty newToken round-trip failed.");

    ExpectProtocolException(() => AuthOkPayloadCodec.Encode(ok with { Result = 0x01 }), "auth_ok unknown result (encode)");
    ExpectProtocolException(() => AuthOkPayloadCodec.Encode(ok with { SessionId = new byte[15] }), "auth_ok short sessionId (encode)");
    ExpectProtocolException(() => AuthOkPayloadCodec.Decode(encoded[..^1]), "auth_ok truncated");
    ExpectProtocolException(() => AuthOkPayloadCodec.Decode([..encoded, 0x00]), "auth_ok extra bytes");

    var badResult = (byte[])encoded.Clone();
    badResult[0] = 0x01;
    ExpectProtocolException(() => AuthOkPayloadCodec.Decode(badResult), "auth_ok unknown result (decode)");

    var tamperedSession = new byte[encoded.Length - 1];
    Array.Copy(encoded, 0, tamperedSession, 0, 10);
    Array.Copy(encoded, 11, tamperedSession, 10, encoded.Length - 11);
    ExpectProtocolException(() => AuthOkPayloadCodec.Decode(tamperedSession), "auth_ok short sessionId (decode)");
}

static void RunAuthDeniedCodecSmokeTests()
{
    var denied = new AuthDeniedPayload(0x01, "auth failed");
    var encoded = AuthDeniedPayloadCodec.Encode(denied);

    byte[] expected =
    [
        0x01, 0x0B,
        0x61, 0x75, 0x74, 0x68, 0x20, 0x66, 0x61, 0x69, 0x6C, 0x65, 0x64
    ];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("AUTH_DENIED wire bytes do not match docs/protocol.md 19.13.");

    var decoded = AuthDeniedPayloadCodec.Decode(encoded);
    if (decoded.Reason != denied.Reason || decoded.Message != denied.Message)
        throw new Exception("AUTH_DENIED round-trip failed.");

    foreach (var reason in new byte[] { 0x01, 0x02, 0x03 })
    {
        var rt = AuthDeniedPayloadCodec.Decode(
            AuthDeniedPayloadCodec.Encode(new AuthDeniedPayload(reason, "x")));
        if (rt.Reason != reason || rt.Message != "x")
            throw new Exception("AUTH_DENIED reason round-trip failed.");
    }

    var emptyMsg = AuthDeniedPayloadCodec.Encode(new AuthDeniedPayload(0x02, ""));
    if (emptyMsg.Length != 2 || emptyMsg[1] != 0x00)
        throw new Exception("AUTH_DENIED empty message must encode length 0.");
    var emptyDecoded = AuthDeniedPayloadCodec.Decode(emptyMsg);
    if (emptyDecoded.Message.Length != 0 || emptyDecoded.Reason != 0x02)
        throw new Exception("AUTH_DENIED empty message round-trip failed.");

    var utf8Msg = AuthDeniedPayloadCodec.Encode(new AuthDeniedPayload(0x01, "échec"));
    if (utf8Msg[1] != 6)
        throw new Exception("AUTH_DENIED message length must be the UTF-8 byte length.");

    ExpectProtocolException(() => AuthDeniedPayloadCodec.Encode(new AuthDeniedPayload(0x04, "x")), "auth_denied unknown reason (encode)");
    ExpectProtocolException(() => AuthDeniedPayloadCodec.Decode(encoded[..^1]), "auth_denied truncated");
    ExpectProtocolException(() => AuthDeniedPayloadCodec.Decode([..encoded, 0x00]), "auth_denied extra bytes");
    ExpectProtocolException(() => AuthDeniedPayloadCodec.Decode([0x04, 0x00]), "auth_denied unknown reason (decode)");
    ExpectProtocolException(() => AuthDeniedPayloadCodec.Decode([0x01, 0x01, 0xFF]), "auth_denied invalid utf8 message");
}

static void RunErrorCodecSmokeTests()
{
    var error = new ErrorPayload(0x02, 0x01, "auth failed");
    var encoded = ErrorPayloadCodec.Encode(error);

    byte[] expected =
    [
        0x02, 0x01, 0x00, 0x0B,
        0x61, 0x75, 0x74, 0x68, 0x20, 0x66, 0x61, 0x69, 0x6C, 0x65, 0x64
    ];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("ERROR wire bytes do not match docs/protocol.md 19.11.");

    var decoded = ErrorPayloadCodec.Decode(encoded);
    if (decoded.Code != error.Code ||
        decoded.Severity != error.Severity ||
        decoded.Message != error.Message)
        throw new Exception("ERROR round-trip failed.");

    foreach (var code in new byte[]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0xFF
    })
    {
        var rt = ErrorPayloadCodec.Decode(
            ErrorPayloadCodec.Encode(new ErrorPayload(code, 0, "x")));
        if (rt.Code != code)
            throw new Exception("ERROR code round-trip failed.");
    }

    foreach (var severity in new byte[] { 0x00, 0x01, 0x02 })
    {
        var rt = ErrorPayloadCodec.Decode(
            ErrorPayloadCodec.Encode(new ErrorPayload(0x01, severity, "x")));
        if (rt.Severity != severity)
            throw new Exception("ERROR severity round-trip failed.");
    }

    var emptyMsg = ErrorPayloadCodec.Encode(new ErrorPayload(0x01, 0x00, ""));
    if (emptyMsg.Length != 4 || emptyMsg[2] != 0x00 || emptyMsg[3] != 0x00)
        throw new Exception("ERROR empty message must encode length 0 (uint16 BE).");
    var emptyDecoded = ErrorPayloadCodec.Decode(emptyMsg);
    if (emptyDecoded.Message.Length != 0)
        throw new Exception("ERROR empty message round-trip failed.");

    var utf8Msg = ErrorPayloadCodec.Encode(new ErrorPayload(0x01, 0x00, "échec"));
    if (utf8Msg[2] != 0x00 || utf8Msg[3] != 6)
        throw new Exception("ERROR messageLength must be uint16 BE UTF-8 byte length.");

    var boundary = new ErrorPayload(0x01, 0x00, new string('a', 1024));
    var boundaryDecoded = ErrorPayloadCodec.Decode(ErrorPayloadCodec.Encode(boundary));
    if (boundaryDecoded.Message.Length != 1024)
        throw new Exception("ERROR 1024-byte message boundary failed.");

    ExpectProtocolException(() => ErrorPayloadCodec.Encode(new ErrorPayload(0x0A, 0x00, "x")), "error unknown code (encode)");
    ExpectProtocolException(() => ErrorPayloadCodec.Encode(new ErrorPayload(0x01, 0x03, "x")), "error unknown severity (encode)");
    ExpectProtocolException(() => ErrorPayloadCodec.Encode(new ErrorPayload(0x01, 0x00, new string('a', 1025))), "error message over 1024 (encode)");
    ExpectProtocolException(() => ErrorPayloadCodec.Decode(encoded[..^1]), "error truncated");
    ExpectProtocolException(() => ErrorPayloadCodec.Decode([..encoded, 0x00]), "error extra bytes");

    var badCode = (byte[])encoded.Clone();
    badCode[0] = 0x0A;
    ExpectProtocolException(() => ErrorPayloadCodec.Decode(badCode), "error unknown code (decode)");

    var badSeverity = (byte[])encoded.Clone();
    badSeverity[1] = 0x03;
    ExpectProtocolException(() => ErrorPayloadCodec.Decode(badSeverity), "error unknown severity (decode)");

    ExpectProtocolException(() => ErrorPayloadCodec.Decode([0x02, 0x01, 0x00, 0x01, 0xFF]), "error invalid utf8 message");

    var overLimit = new byte[] { 0x02, 0x01, 0x04, 0x01 }.Concat(new byte[1025]).ToArray();
    ExpectProtocolException(() => ErrorPayloadCodec.Decode(overLimit), "error message over 1024 (decode)");
}

static void RunDisconnectCodecSmokeTests()
{
    var disconnect = new DisconnectPayload(0x00);
    var encoded = DisconnectPayloadCodec.Encode(disconnect);

    byte[] expected = [0x00];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("DISCONNECT wire bytes do not match docs/protocol.md 19.10.");

    var decoded = DisconnectPayloadCodec.Decode(encoded);
    if (decoded.Reason != 0x00)
        throw new Exception("DISCONNECT round-trip failed.");

    foreach (var reason in new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 })
    {
        var rt = DisconnectPayloadCodec.Decode(
            DisconnectPayloadCodec.Encode(new DisconnectPayload(reason)));
        if (rt.Reason != reason)
            throw new Exception("DISCONNECT reason round-trip failed.");
    }

    ExpectProtocolException(() => DisconnectPayloadCodec.Encode(new DisconnectPayload(0x06)), "disconnect unknown reason (encode)");
    ExpectProtocolException(() => DisconnectPayloadCodec.Decode([0x06]), "disconnect unknown reason (decode)");
    ExpectProtocolException(() => DisconnectPayloadCodec.Decode([]), "disconnect truncated");
    ExpectProtocolException(() => DisconnectPayloadCodec.Decode([0x00, 0x00]), "disconnect extra bytes");
}

static void RunHeartbeatCodecSmokeTests()
{
    var heartbeat = new HeartbeatPayload(0x0000018D9E8E2C00UL);
    var encoded = HeartbeatPayloadCodec.Encode(heartbeat);

    byte[] expected = [0x00, 0x00, 0x01, 0x8D, 0x9E, 0x8E, 0x2C, 0x00];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("HEARTBEAT wire bytes do not match docs/protocol.md 19.9.");

    var decoded = HeartbeatPayloadCodec.Decode(encoded);
    if (decoded.ClientSendTime != heartbeat.ClientSendTime)
        throw new Exception("HEARTBEAT round-trip failed.");

    ExpectProtocolException(() => HeartbeatPayloadCodec.Decode(encoded[..^1]), "heartbeat truncated");
    ExpectProtocolException(() => HeartbeatPayloadCodec.Decode([..encoded, 0x00]), "heartbeat extra bytes");
}

static void RunPongCodecSmokeTests()
{
    var pong = new PongPayload(0x0000018D9E8E2C00UL, 0x0000018D9E8E2C05UL);
    var encoded = PongPayloadCodec.Encode(pong);

    byte[] expected =
    [
        0x00, 0x00, 0x01, 0x8D, 0x9E, 0x8E, 0x2C, 0x00,
        0x00, 0x00, 0x01, 0x8D, 0x9E, 0x8E, 0x2C, 0x05
    ];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("PONG wire bytes do not match docs/protocol.md 19.9.");

    var decoded = PongPayloadCodec.Decode(encoded);
    if (decoded.ClientSendTime != pong.ClientSendTime ||
        decoded.ServerTime != pong.ServerTime)
        throw new Exception("PONG round-trip failed.");

    ExpectProtocolException(() => PongPayloadCodec.Decode(encoded[..^1]), "pong truncated");
    ExpectProtocolException(() => PongPayloadCodec.Decode([..encoded, 0x00]), "pong extra bytes");
}

static void RunAckCodecSmokeTests()
{
    var ack = new AckPayload(0x1234, 0x0000018D9E8E2C00UL);
    var encoded = AckPayloadCodec.Encode(ack);

    byte[] expected =
    [
        0x12, 0x34,
        0x00, 0x00, 0x01, 0x8D, 0x9E, 0x8E, 0x2C, 0x00
    ];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("ACK wire bytes do not match docs/protocol.md 11.");

    var decoded = AckPayloadCodec.Decode(encoded);
    if (decoded != ack)
        throw new Exception("ACK round-trip failed.");

    ExpectProtocolException(() => AckPayloadCodec.Decode(encoded[..^1]), "ack truncated");
    ExpectProtocolException(() => AckPayloadCodec.Decode([..encoded, 0x00]), "ack extra bytes");
    ExpectProtocolException(() => AckPayloadCodec.Decode([]), "ack empty payload");
}

static void RunInputEventCodecSmokeTests()
{
    var button = new InputEvent(
        "btn-fire",
        InputEventCodec.KindButton,
        InputEventCodec.FlagStateChanged,
        State: InputEventCodec.StateDown,
        PressCount: 1);
    var buttonEncoded = InputEventPayloadCodec.Encode(new InputEventPayload(button));

    byte[] buttonExpected =
    [
        0x08,
        0x62, 0x74, 0x6E, 0x2D, 0x66, 0x69, 0x72, 0x65,
        0x00, 0x01, 0x01, 0x00, 0x01
    ];
    if (!buttonEncoded.SequenceEqual(buttonExpected))
        throw new Exception("INPUT_EVENT button wire bytes do not match docs/protocol.md 19.5.");
    var buttonDecoded = InputEventPayloadCodec.Decode(buttonEncoded);
    if (buttonDecoded.Event != button)
        throw new Exception("INPUT_EVENT button round-trip failed.");

    var axis = new InputEvent(
        "thr", InputEventCodec.KindAxis, InputEventCodec.FlagStateChanged, Value: 0.5f);
    var axisEncoded = InputEventPayloadCodec.Encode(new InputEventPayload(axis));

    byte[] axisExpected =
    [
        0x03, 0x74, 0x68, 0x72,
        0x01, 0x01,
        0x3F, 0x00, 0x00, 0x00
    ];
    if (!axisEncoded.SequenceEqual(axisExpected))
        throw new Exception("INPUT_EVENT axis wire bytes do not match docs/protocol.md 19.6.");
    var axisDecoded = InputEventPayloadCodec.Decode(axisEncoded);
    if (axisDecoded.Event != axis)
        throw new Exception("INPUT_EVENT axis round-trip failed.");

    var stick = new InputEvent(
        "rs", InputEventCodec.KindStick, InputEventCodec.FlagStateChanged, X: -0.5f, Y: 0.25f);
    var stickEncoded = InputEventPayloadCodec.Encode(new InputEventPayload(stick));

    byte[] stickExpected =
    [
        0x02, 0x72, 0x73,
        0x02, 0x01,
        0xBF, 0x00, 0x00, 0x00,
        0x3E, 0x80, 0x00, 0x00
    ];
    if (!stickEncoded.SequenceEqual(stickExpected))
        throw new Exception("INPUT_EVENT stick wire bytes do not match docs/protocol.md 19.7.");
    var stickDecoded = InputEventPayloadCodec.Decode(stickEncoded);
    if (stickDecoded.Event != stick)
        throw new Exception("INPUT_EVENT stick round-trip failed.");

    var trigger = new InputEvent(
        "t1", InputEventCodec.KindTrigger, InputEventCodec.FlagStateChanged, Value: 1.0f);
    var triggerEncoded = InputEventPayloadCodec.Encode(new InputEventPayload(trigger));

    byte[] triggerExpected =
    [
        0x02, 0x74, 0x31,
        0x03, 0x01,
        0x3F, 0x80, 0x00, 0x00
    ];
    if (!triggerEncoded.SequenceEqual(triggerExpected))
        throw new Exception("INPUT_EVENT trigger wire bytes do not match docs/protocol.md 9.");
    var triggerDecoded = InputEventPayloadCodec.Decode(triggerEncoded);
    if (triggerDecoded.Event != trigger)
        throw new Exception("INPUT_EVENT trigger round-trip failed.");

    var hat = new InputEvent(
        "dpad", InputEventCodec.KindHat, InputEventCodec.FlagStateChanged, HatValue: 1);
    var hatEncoded = InputEventPayloadCodec.Encode(new InputEventPayload(hat));

    byte[] hatExpected =
    [
        0x04, 0x64, 0x70, 0x61, 0x64,
        0x04, 0x01,
        0x01
    ];
    if (!hatEncoded.SequenceEqual(hatExpected))
        throw new Exception("INPUT_EVENT hat wire bytes do not match docs/protocol.md 9.");
    var hatDecoded = InputEventPayloadCodec.Decode(hatEncoded);
    if (hatDecoded.Event != hat)
        throw new Exception("INPUT_EVENT hat round-trip failed.");

    for (byte hv = 0; hv <= 8; hv++)
    {
        var e = new InputEvent("h", InputEventCodec.KindHat, 0, HatValue: hv);
        var rt = InputEventPayloadCodec.Decode(InputEventPayloadCodec.Encode(new InputEventPayload(e)));
        if (rt.Event != e)
            throw new Exception("INPUT_EVENT hat value round-trip failed.");
    }

    var utf8Id = new InputEvent(
        "é", InputEventCodec.KindButton, 0, State: InputEventCodec.StateUp, PressCount: 0);
    var utf8Encoded = InputEventPayloadCodec.Encode(new InputEventPayload(utf8Id));
    if (utf8Encoded[0] != 2)
        throw new Exception("controlId length must be the UTF-8 byte length.");

    var maxId = new InputEvent(
        new string('a', 64), InputEventCodec.KindButton, 0,
        State: InputEventCodec.StateUp, PressCount: 0);
    InputEventPayloadCodec.Decode(InputEventPayloadCodec.Encode(new InputEventPayload(maxId)));

    ExpectProtocolException(() => InputEventPayloadCodec.Encode(new InputEventPayload(
        new InputEvent(new string('a', 65), InputEventCodec.KindButton, 0,
            State: InputEventCodec.StateUp, PressCount: 0))), "input_event controlId over 64 (encode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x41, ..new byte[65], 0x00, 0x00, 0x00, 0x00, 0x00]), "input_event controlId over 64 (decode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x00, 0x00, 0x00, 0x00, 0x00, 0x00]), "input_event controlId zero length");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x01, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00]), "input_event invalid utf8 controlId");

    ExpectProtocolException(() => InputEventPayloadCodec.Encode(new InputEventPayload(
        new InputEvent("a", 0x05, 0))), "input_event unknown kind (encode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x01, 0x61, 0x05, 0x00]), "input_event unknown kind (decode)");

    ExpectProtocolException(() => InputEventPayloadCodec.Encode(new InputEventPayload(
        new InputEvent("a", InputEventCodec.KindButton, 0x04,
            State: InputEventCodec.StateUp, PressCount: 0))), "input_event invalid flags (encode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x01, 0x61, 0x00, 0x04, 0x00, 0x00, 0x00]), "input_event invalid flags (decode)");

    ExpectProtocolException(() => InputEventPayloadCodec.Encode(new InputEventPayload(
        new InputEvent("a", InputEventCodec.KindButton, 0, State: 0x02, PressCount: 0))),
        "input_event invalid button state (encode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x01, 0x61, 0x00, 0x00, 0x02, 0x00, 0x00]), "input_event invalid button state (decode)");

    ExpectProtocolException(() => InputEventPayloadCodec.Encode(new InputEventPayload(
        new InputEvent("a", InputEventCodec.KindAxis, 0, Value: float.NaN))),
        "input_event NaN axis (encode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x01, 0x61, 0x01, 0x00, 0x7F, 0xC0, 0x00, 0x00]), "input_event NaN axis (decode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x01, 0x61, 0x01, 0x00, 0x7F, 0x80, 0x00, 0x00]), "input_event +Inf axis (decode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Encode(new InputEventPayload(
        new InputEvent("a", InputEventCodec.KindAxis, 0, Value: 1.5f))),
        "input_event axis over range (encode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x01, 0x61, 0x01, 0x00, 0x3F, 0xC0, 0x00, 0x00]), "input_event axis over range (decode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Encode(new InputEventPayload(
        new InputEvent("a", InputEventCodec.KindAxis, 0, Value: -0.01f))),
        "input_event axis under range (encode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x01, 0x61, 0x01, 0x00, 0xBC, 0x23, 0xD7, 0x0A]), "input_event axis under range (decode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Encode(new InputEventPayload(
        new InputEvent("a", InputEventCodec.KindStick, 0, X: -1.01f, Y: 0))),
        "input_event stick out of range (encode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x01, 0x61, 0x02, 0x00, 0xBF, 0x81, 0x47, 0xAE, 0x00, 0x00, 0x00, 0x00]),
        "input_event stick out of range (decode)");

    ExpectProtocolException(() => InputEventPayloadCodec.Encode(new InputEventPayload(
        new InputEvent("a", InputEventCodec.KindHat, 0, HatValue: 9))),
        "input_event hat 9 (encode)");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(
        [0x01, 0x61, 0x04, 0x00, 0x09]), "input_event hat 9 (decode)");

    ExpectProtocolException(() => InputEventPayloadCodec.Decode(buttonEncoded[..^1]), "input_event button truncated");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode([..buttonEncoded, 0x00]), "input_event button extra bytes");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(axisEncoded[..^1]), "input_event axis truncated");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(stickEncoded[..^1]), "input_event stick truncated");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(triggerEncoded[..^1]), "input_event trigger truncated");
    ExpectProtocolException(() => InputEventPayloadCodec.Decode(hatEncoded[..^1]), "input_event hat truncated");
}

static void RunInputSnapshotCodecSmokeTests()
{
    var snapshot = new InputSnapshotPayload(new List<InputEvent>
    {
        new("btn-fire", InputEventCodec.KindButton, InputEventCodec.FlagStateChanged,
            State: InputEventCodec.StateDown, PressCount: 5),
        new("rs", InputEventCodec.KindStick, InputEventCodec.FlagStateChanged, X: 0f, Y: 0f)
    });
    var encoded = InputSnapshotPayloadCodec.Encode(snapshot);

    byte[] expected =
    [
        0x00, 0x02,
        0x08,
        0x62, 0x74, 0x6E, 0x2D, 0x66, 0x69, 0x72, 0x65,
        0x00, 0x01, 0x01, 0x00, 0x05,
        0x02, 0x72, 0x73,
        0x02, 0x01,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    ];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("INPUT_SNAPSHOT wire bytes do not match docs/protocol.md 19.8.");

    var decoded = InputSnapshotPayloadCodec.Decode(encoded);
    if (decoded.Events.Count != snapshot.Events.Count)
        throw new Exception("INPUT_SNAPSHOT round-trip failed (count).");
    for (var i = 0; i < decoded.Events.Count; i++)
    {
        if (decoded.Events[i] != snapshot.Events[i])
            throw new Exception("INPUT_SNAPSHOT round-trip failed.");
    }

    var single = new InputSnapshotPayload(new List<InputEvent>
    {
        new("a", InputEventCodec.KindButton, 0, State: InputEventCodec.StateUp, PressCount: 0)
    });
    var singleDecoded = InputSnapshotPayloadCodec.Decode(InputSnapshotPayloadCodec.Encode(single));
    if (singleDecoded.Events.Count != 1 || singleDecoded.Events[0] != single.Events[0])
        throw new Exception("INPUT_SNAPSHOT single entry round-trip failed.");

    ExpectProtocolException(() => InputSnapshotPayloadCodec.Encode(
        new InputSnapshotPayload(new List<InputEvent>())), "snapshot empty (encode)");

    var many = Enumerable.Range(0, 1025)
        .Select(i => new InputEvent("a", InputEventCodec.KindButton, 0,
            State: InputEventCodec.StateUp, PressCount: 0))
        .ToList();
    ExpectProtocolException(() => InputSnapshotPayloadCodec.Encode(
        new InputSnapshotPayload(many)), "snapshot over 1024 (encode)");
    ExpectProtocolException(() => InputSnapshotPayloadCodec.Decode([0x00, 0x00]), "snapshot zero entries (decode)");
    ExpectProtocolException(() => InputSnapshotPayloadCodec.Decode(
        [0x04, 0x01, 0x01, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00]), "snapshot over 1024 (decode)");
    ExpectProtocolException(() => InputSnapshotPayloadCodec.Decode(encoded[..^1]), "snapshot truncated");
    ExpectProtocolException(() => InputSnapshotPayloadCodec.Decode([..encoded, 0x00]), "snapshot extra bytes");
    ExpectProtocolException(() => InputSnapshotPayloadCodec.Decode(
        [0x00, 0x01, 0x01, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00]), "snapshot invalid utf8 controlId");
}

static void RunInputResetCodecSmokeTests()
{
    foreach (var reason in new byte[] { 0x00, 0x01, 0x02 })
    {
        var rt = InputResetPayloadCodec.Decode(
            InputResetPayloadCodec.Encode(new InputResetPayload(reason)));
        if (rt.Reason != reason)
            throw new Exception("INPUT_RESET reason round-trip failed.");
    }

    var encoded = InputResetPayloadCodec.Encode(new InputResetPayload(0x00));
    byte[] expected = [0x00];
    if (!encoded.SequenceEqual(expected))
        throw new Exception("INPUT_RESET wire bytes do not match docs/protocol.md 16.");

    ExpectProtocolException(() => InputResetPayloadCodec.Encode(new InputResetPayload(0x03)), "input_reset unknown reason (encode)");
    ExpectProtocolException(() => InputResetPayloadCodec.Decode([0x03]), "input_reset unknown reason (decode)");
    ExpectProtocolException(() => InputResetPayloadCodec.Decode([]), "input_reset truncated");
    ExpectProtocolException(() => InputResetPayloadCodec.Decode([0x00, 0x00]), "input_reset extra bytes");
}

static void ExpectProtocolException(Action action, string name)
{
    try
    {
        action();
        throw new Exception($"Expected ProtocolException for {name}.");
    }
    catch (ProtocolException)
    {
    }
}
