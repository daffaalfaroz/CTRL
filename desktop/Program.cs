using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CTRL.Desktop.Input;
using CTRL.Desktop.Input.Win32;
using CTRL.Desktop.Protocol;
using CTRL.Desktop.Session;
using CTRL.Desktop.Transport;

if (args.Length >= 1 && args[0] == "--integration")
{
    var port = args.Length >= 2 && int.TryParse(args[1], out var p) ? p : 0;
    return await RunIntegrationServerAsync(port);
}

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
RunFrameBufferSmokeTests();
await RunLoopbackTransportSmokeTests();
RunSequenceTrackerSmokeTests();
RunAckTrackerSmokeTests();
RunSessionHandshakeSmokeTests();
RunSessionForbiddenFlagSmokeTests();
RunSessionNotAuthenticatedSmokeTests();
RunSessionInvalidMessageSmokeTests();
RunSessionUnsupportedMessageSmokeTests();
RunSessionWrongDirectionSmokeTests();
RunSessionInputDropSmokeTests();
RunSessionPongSmokeTests();
RunSessionAckSmokeTests();
RunSessionAuthDeniedSmokeTests();
RunSessionTakeoverSmokeTests();
await RunSessionLoopbackHostSmokeTests();
RunSequenceWrapSmokeTests();
RunSessionStateTransitionSmokeTests();
RunSessionAckLifecycleSmokeTests();
RunSessionHeartbeatTimeoutSmokeTests();
RunSessionDisconnectFlushSmokeTests();
RunSessionSnapshotBoundarySmokeTests();
RunSessionSequenceBoundarySmokeTests();
RunHmacVectorSmokeTests();
RunRealAuthTokenLifecycleSmokeTests();
RunPairingLifecycleSmokeTests();
RunLockoutSmokeTests();
RunOutputSinkSmokeTests();
RunWin32MapperSmokeTests();
RunMouseSinkSmokeTests();
await RunInputReliabilitySmokeTests();
RunGamepadInputSmokeTests();

Console.WriteLine("M1.1 + M1.2.1 + M1.2.2 + M1.2.3 + M1.3 protocol smoke tests passed.");
Console.WriteLine("M1.4.2 session/connection state machine smoke tests passed.");
Console.WriteLine("M1.4.3 session integration hardening smoke tests passed.");
Console.WriteLine("M1.4.4 real authentication smoke tests passed.");
Console.WriteLine("M2.0 input architecture smoke tests passed.");
return 0;

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

    // D2: Decode must NOT reject reserved flags (the session layer answers with
    // ERROR forbidden + close); Encode must still reject them defensively.
    var reserved = FrameCodec.Decode([..encoded[..4], 0x02, ..encoded[5..]]);
    if ((reserved.Flags & FrameCodec.ReservedFlagsMask) == 0)
        throw new Exception("Decode must preserve reserved flags (D2).");
    ExpectProtocolException(
        () => FrameCodec.Encode(frame with { Flags = FrameCodec.ReservedFlagsMask }), "reserved flag (encode)");
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

    var oneByte = new HelloPayload("a", "0.1.0", 1, 0, 0x00000007);
    var oneByteDecoded = HelloPayloadCodec.Decode(HelloPayloadCodec.Encode(oneByte));
    if (oneByteDecoded != oneByte)
        throw new Exception("HELLO 1-byte deviceId round-trip failed.");

    var sixtyFour = new HelloPayload(new string('a', 64), new string('b', 64), 1, 0, 0x00000007);
    var sixtyFourDecoded = HelloPayloadCodec.Decode(HelloPayloadCodec.Encode(sixtyFour));
    if (sixtyFourDecoded != sixtyFour)
        throw new Exception("HELLO 64-byte deviceId/clientVersion round-trip failed.");

    var multiByte = new HelloPayload("é", "c", 1, 0, 0x00000007);
    var multiByteBytes = HelloPayloadCodec.Encode(multiByte);
    if (multiByteBytes[0] != 2)
        throw new Exception("HELLO deviceId length must be UTF-8 byte count.");

    ExpectProtocolException(() => HelloPayloadCodec.Encode(new HelloPayload("", "0.1.0", 1, 0, 0x00000007)), "hello empty deviceId (encode)");
    ExpectProtocolException(() => HelloPayloadCodec.Encode(new HelloPayload(new string('a', 65), "0.1.0", 1, 0, 0x00000007)), "hello 65-byte deviceId (encode)");
    ExpectProtocolException(() => HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "", 1, 0, 0x00000007)), "hello empty clientVersion (encode)");
    ExpectProtocolException(() => HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", new string('c', 65), 1, 0, 0x00000007)), "hello 65-byte clientVersion (encode)");
    ExpectProtocolException(() => HelloPayloadCodec.Decode(encoded[..^1]), "truncated hello");
    ExpectProtocolException(() => HelloPayloadCodec.Decode([..encoded, 0x00]), "hello extra bytes");
    ExpectProtocolException(() => HelloPayloadCodec.Decode([0x01, 0xFF, 0x00, 0x00, 0x00, 0x00]), "hello invalid utf8");
    ExpectProtocolException(() => HelloPayloadCodec.Decode([0x03, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00]), "hello bad length prefix");
    ExpectProtocolException(() => HelloPayloadCodec.Decode([0x00]), "hello empty deviceId (decode)");
    ExpectProtocolException(() => HelloPayloadCodec.Decode([0x41]), "hello 65-byte deviceId (decode)");
    ExpectProtocolException(() => HelloPayloadCodec.Decode([0x09, 0x63, 0x74, 0x72, 0x6C, 0x2D, 0x34, 0x32, 0x61, 0x38, 0x00]), "hello empty clientVersion (decode)");
    ExpectProtocolException(() => HelloPayloadCodec.Decode([0x09, 0x63, 0x74, 0x72, 0x6C, 0x2D, 0x34, 0x32, 0x61, 0x38, 0x41]), "hello 65-byte clientVersion (decode)");
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

    var oneByteName = new WelcomePayload("a", 1, 0, 1, new byte[16], true, new byte[32]);
    var oneByteNameDecoded = WelcomePayloadCodec.Decode(WelcomePayloadCodec.Encode(oneByteName));
    if (oneByteNameDecoded.ServerName != "a")
        throw new Exception("WELCOME 1-byte serverName round-trip failed.");

    var sixtyFourName = new WelcomePayload(new string('s', 64), 1, 0, 1, new byte[16], true, new byte[32]);
    var sixtyFourNameDecoded = WelcomePayloadCodec.Decode(WelcomePayloadCodec.Encode(sixtyFourName));
    if (sixtyFourNameDecoded.ServerName.Length != 64)
        throw new Exception("WELCOME 64-byte serverName round-trip failed.");

    var multiByteName = new WelcomePayload("é", 1, 0, 1, new byte[16], true, new byte[32]);
    var multiByteNameBytes = WelcomePayloadCodec.Encode(multiByteName);
    if (multiByteNameBytes[0] != 2)
        throw new Exception("WELCOME serverName length must be UTF-8 byte count.");

    ExpectProtocolException(() => WelcomePayloadCodec.Encode(new WelcomePayload("", 1, 0, 1, new byte[16], true, new byte[32])), "welcome empty serverName (encode)");
    ExpectProtocolException(() => WelcomePayloadCodec.Encode(new WelcomePayload(new string('s', 65), 1, 0, 1, new byte[16], true, new byte[32])), "welcome 65-byte serverName (encode)");
    ExpectProtocolException(() => WelcomePayloadCodec.Decode([0x00]), "welcome empty serverName (decode)");
    ExpectProtocolException(() => WelcomePayloadCodec.Decode([0x41]), "welcome 65-byte serverName (decode)");

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

    var oneByteId = new AuthPayload(0x02, "", "d", new byte[32]);
    var oneByteIdDecoded = AuthPayloadCodec.Decode(AuthPayloadCodec.Encode(oneByteId));
    if (oneByteIdDecoded.DeviceId != "d")
        throw new Exception("AUTH 1-byte deviceId round-trip failed.");

    var sixtyFourId = new AuthPayload(0x02, "", new string('d', 64), new byte[32]);
    var sixtyFourIdDecoded = AuthPayloadCodec.Decode(AuthPayloadCodec.Encode(sixtyFourId));
    if (sixtyFourIdDecoded.DeviceId.Length != 64)
        throw new Exception("AUTH 64-byte deviceId round-trip failed.");

    var multiByteId = new AuthPayload(0x02, "", "dé", new byte[32]);
    var multiByteIdBytes = AuthPayloadCodec.Encode(multiByteId);
    if (multiByteIdBytes[1] != 0x00 || multiByteIdBytes[2] != 3)
        throw new Exception("AUTH deviceId length must be UTF-8 byte count.");

    ExpectProtocolException(() => AuthPayloadCodec.Encode(new AuthPayload(0x03, "", "d", new byte[32])), "auth unknown credentialType (encode)");
    ExpectProtocolException(() => AuthPayloadCodec.Encode(new AuthPayload(0x01, "x", "d", new byte[32])), "auth token with non-empty credential (encode)");
    ExpectProtocolException(() => AuthPayloadCodec.Encode(new AuthPayload(0x02, "", "d", new byte[31])), "auth short challengeResponse (encode)");
    ExpectProtocolException(() => AuthPayloadCodec.Encode(new AuthPayload(0x02, new string('a', 256), "d", new byte[32])), "auth credential over 255 bytes (encode)");
    ExpectProtocolException(() => AuthPayloadCodec.Encode(new AuthPayload(0x02, "", "", new byte[32])), "auth empty deviceId (encode)");
    ExpectProtocolException(() => AuthPayloadCodec.Encode(new AuthPayload(0x02, "", new string('d', 65), new byte[32])), "auth 65-byte deviceId (encode)");
    ExpectProtocolException(() => AuthPayloadCodec.Decode(encoded[..^1]), "auth truncated");
    ExpectProtocolException(() => AuthPayloadCodec.Decode([..encoded, 0x00]), "auth extra bytes");
    ExpectProtocolException(() => AuthPayloadCodec.Decode([0x02, 0x00, 0x00]), "auth empty deviceId (decode)");
    ExpectProtocolException(() => AuthPayloadCodec.Decode([0x02, 0x00, 0x41]), "auth 65-byte deviceId (decode)");

    var badType = (byte[])encoded.Clone();
    badType[0] = 0x03;
    ExpectProtocolException(() => AuthPayloadCodec.Decode(badType), "auth unknown credentialType (decode)");

    var badTokenCredential = new byte[] { 0x01, 0x01, 0x41, 0x01, 0x64 }.Concat(new byte[32]).ToArray();
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
        [0x61, 0x31, 0x62, 0x32, 0x63, 0x33, 0x64, 0x34, 0x65, 0x35,
         0x66, 0x36, 0x30, 0x37, 0x31, 0x38]);
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
        !decoded.NewToken.SequenceEqual(ok.NewToken))
        throw new Exception("AUTH_OK round-trip failed.");

    var noToken = new AuthOkPayload(0x00, new byte[16], 0x00000007, []);
    var noTokenBytes = AuthOkPayloadCodec.Encode(noToken);
    if (noTokenBytes[^1] != 0x00)
        throw new Exception("AUTH_OK empty newToken must encode length 0.");
    var noTokenDecoded = AuthOkPayloadCodec.Decode(noTokenBytes);
    if (noTokenDecoded.NewToken.Length != 0 ||
        !noTokenDecoded.SessionId.SequenceEqual(noToken.SessionId) ||
        noTokenDecoded.ServerCapabilities != noToken.ServerCapabilities)
        throw new Exception("AUTH_OK empty newToken round-trip failed.");

    byte[] binaryToken = [0x00, 0x80, 0xFF, 0x10, 0xFE];
    var binaryOk = new AuthOkPayload(0x00, new byte[16], 0x00000007, binaryToken);
    var binaryDecoded = AuthOkPayloadCodec.Decode(AuthOkPayloadCodec.Encode(binaryOk));
    if (!binaryDecoded.NewToken.SequenceEqual(binaryToken))
        throw new Exception("AUTH_OK raw binary newToken round-trip failed.");

    var maxToken = new AuthOkPayload(0x00, new byte[16], 0x00000007, new byte[255]);
    var maxDecoded = AuthOkPayloadCodec.Decode(AuthOkPayloadCodec.Encode(maxToken));
    if (maxDecoded.NewToken.Length != 255)
        throw new Exception("AUTH_OK 255-byte newToken boundary failed.");

    ExpectProtocolException(() => AuthOkPayloadCodec.Encode(ok with { Result = 0x01 }), "auth_ok unknown result (encode)");
    ExpectProtocolException(() => AuthOkPayloadCodec.Encode(ok with { SessionId = new byte[15] }), "auth_ok short sessionId (encode)");
    ExpectProtocolException(() => AuthOkPayloadCodec.Encode(new AuthOkPayload(0x00, new byte[16], 0x00000007, new byte[256])), "auth_ok newToken over 255 bytes (encode)");
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

static void RunFrameBufferSmokeTests()
{
    var feed = FrameCodec.Encode(MakeEchoFrame(0x0012, [0x10, 0x20]));
    if (feed.Length != ProtocolFrame.HeaderSize + 2)
        throw new Exception("Unexpected frame feed length.");

    var perByte = new FrameBuffer();
    for (var i = 0; i < feed.Length - 1; i++)
    {
        perByte.Append(new[] { feed[i] });
        if (perByte.TryReadFrame(out _) || !perByte.HasBufferedData)
            throw new Exception("FrameBuffer: premature frame during per-byte feed.");
    }
    perByte.Append(new[] { feed[^1] });
    if (!perByte.TryReadFrame(out var whole) || !whole.SequenceEqual(feed))
        throw new Exception("FrameBuffer: per-byte frame mismatch.");
    if (perByte.HasBufferedData)
        throw new Exception("FrameBuffer: trailing data after per-byte frame.");

    var payloadSplit = new FrameBuffer();
    payloadSplit.Append(feed.Take(feed.Length - 1).ToArray());
    if (payloadSplit.TryReadFrame(out _) || !payloadSplit.HasBufferedData)
        throw new Exception("FrameBuffer: frame read before full payload.");
    payloadSplit.Append(feed.Skip(feed.Length - 1).ToArray());
    if (!payloadSplit.TryReadFrame(out var frame1) || !frame1.SequenceEqual(feed))
        throw new Exception("FrameBuffer: frame not read after payload completion.");
    if (payloadSplit.HasBufferedData)
        throw new Exception("FrameBuffer: trailing data after single frame.");

    for (var split = 0; split <= feed.Length; split++)
    {
        var f = new FrameBuffer();
        f.Append(feed.Take(split).ToArray());
        f.Append(feed.Skip(split).ToArray());
        if (!f.TryReadFrame(out var got) || !got.SequenceEqual(feed))
            throw new Exception($"FrameBuffer: split at index {split} failed.");
    }

    var multi = new FrameBuffer();
    var expected = new List<byte[]>();
    var chunk = new List<byte>();
    for (var i = 0; i < 3; i++)
    {
        var encoded = FrameCodec.Encode(MakeEchoFrame((ushort)(0x0001 + i), [(byte)i]));
        chunk.AddRange(encoded);
        expected.Add(encoded);
    }
    multi.Append(chunk.ToArray());
    for (var i = 0; i < 3; i++)
    {
        if (!multi.TryReadFrame(out var got) || !got.SequenceEqual(expected[i]))
            throw new Exception("FrameBuffer: multiple frames per chunk failed.");
    }
    if (multi.HasBufferedData)
        throw new Exception("FrameBuffer: leftover data after multiple frames.");

    var framePlusPartial = new FrameBuffer();
    var one = FrameCodec.Encode(MakeEchoFrame(0x01, [0xAA]));
    var two = FrameCodec.Encode(MakeEchoFrame(0x02, [0xBB]));
    framePlusPartial.Append(one.Concat(two.Take(7)).ToArray());
    if (!framePlusPartial.TryReadFrame(out var first) || !first.SequenceEqual(one))
        throw new Exception("FrameBuffer: frame + partial next failed (first).");
    if (!framePlusPartial.HasBufferedData)
        throw new Exception("FrameBuffer: partial next frame not buffered.");
    framePlusPartial.Append(two.Skip(7).ToArray());
    if (!framePlusPartial.TryReadFrame(out var second) || !second.SequenceEqual(two))
        throw new Exception("FrameBuffer: frame + partial next failed (second).");
    if (framePlusPartial.HasBufferedData)
        throw new Exception("FrameBuffer: leftover after frame + partial.");

    var bulk = new FrameBuffer();
    for (var i = 0; i < 100; i++)
        bulk.Append(FrameCodec.Encode(MakeEchoFrame((ushort)(0x0001 + i), [(byte)i])));
    for (var i = 0; i < 100; i++)
    {
        if (!bulk.TryReadFrame(out _))
            throw new Exception("FrameBuffer: bulk frame missing.");
    }
    if (bulk.HasBufferedData)
        throw new Exception("FrameBuffer: bulk leftover.");

    Console.WriteLine("M1.3: frame buffer smoke tests passed.");
}

static async Task RunLoopbackTransportSmokeTests()
{
    var server = new TcpServer(0);
    var received = new ConcurrentQueue<ProtocolFrame>();
    var disconnected = new ConcurrentQueue<string>();

    server.ClientConnected += async connection =>
    {
        Assert(connection.NoDelay, "server NoDelay not set");
        connection.FrameReceived += frame => received.Enqueue(frame);
        connection.Disconnected += reason => disconnected.Enqueue(reason);
        await connection.SendAsync(FrameCodec.Encode(MakeEchoFrame(0x01, [0x55])));
    };

    _ = server.StartAsync();
    await WaitUntil(() => server.LocalPort > 0);
    Assert(server.LocalPort > 0, "server ephemeral port not assigned");

    var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, server.LocalPort);
    var transport = new TcpConnection(client);
    Assert(transport.NoDelay, "client NoDelay not set");
    var clientFrames = new ConcurrentQueue<ProtocolFrame>();
    var clientDisconnected = new ConcurrentQueue<string>();
    transport.FrameReceived += frame => clientFrames.Enqueue(frame);
    transport.Disconnected += reason => clientDisconnected.Enqueue(reason);
    var run = transport.RunAsync();

    await transport.SendAsync(FrameCodec.Encode(MakeEchoFrame(0x02, [0x66])));
    await WaitUntil(() => received.Count == 1 && clientFrames.Count == 1);
    Assert(received.Count == 1, "server did not receive echo");
    Assert(clientFrames.Count == 1, "client did not receive echo");
    Assert(FramesEqual(received.First(), MakeEchoFrame(0x02, [0x66])), "server echo mismatch");
    Assert(FramesEqual(clientFrames.First(), MakeEchoFrame(0x01, [0x55])), "client echo mismatch");

    await transport.CloseAsync();
    await run;
    await WaitUntil(() => clientDisconnected.Count == 1);
    Assert(clientDisconnected.Count == 1, "client disconnected not reported exactly once");
    await WaitUntil(() => disconnected.Count == 1);
    Assert(disconnected.Count == 1, "server disconnected not reported exactly once");

    await server.StopAsync();

    Console.WriteLine("M1.3: loopback transport smoke tests passed.");
}

static void RunSequenceTrackerSmokeTests()
{
    var outbound = new SequenceTracker();
    Assert(outbound.Next() == 0 && outbound.Next() == 1 && outbound.Next() == 2,
        "outbound sequence must be continuous from 0");

    var inbound = new InboundSequenceTracker();
    Assert(inbound.IsMonotonic(0), "first inbound sequence must be accepted");
    Assert(inbound.IsMonotonic(1), "increment must be accepted");
    Assert(inbound.IsMonotonic(2), "increment must be accepted");
    Assert(!inbound.IsMonotonic(2), "duplicate sequence must be rejected");
    Assert(!inbound.IsMonotonic(0), "regression must be rejected");
    Assert(!inbound.IsMonotonic(40000), "forward jump over 0x7FFF must be rejected");
    Assert(inbound.IsMonotonic(40001), "forward jump within 0x7FFF must be accepted");

    var wrap = new InboundSequenceTracker();
    for (ushort i = 0; i < 3; i++)
        Assert(wrap.IsMonotonic(i), "wrapped increment must be accepted");

    Console.WriteLine("M1.4.2: sequence tracker smoke tests passed.");
}

static void RunAckTrackerSmokeTests()
{
    ulong now = 0;
    var tracker = new AckTracker(() => now, 3000);
    tracker.Track(10);
    Assert(tracker.PendingCount == 1, "pending count after track");
    now = 2999;
    Assert(tracker.RetryExpired().Count == 0, "no retry before timeout");
    now = 3000;
    var retry = tracker.RetryExpired();
    Assert(retry.Count == 1 && retry[0] == 10, "retry after first timeout");
    Assert(tracker.RetryExpired().Count == 0, "retry exactly once");
    now = 6000;
    var failed = tracker.Failed();
    Assert(failed.Count == 1 && failed[0] == 10, "failed after retry timeout");

    now = 0;
    var tracker2 = new AckTracker(() => now, 3000);
    tracker2.Track(20);
    tracker2.Acknowledge(20);
    Assert(tracker2.PendingCount == 0 && !tracker2.IsPending(20), "ack must clear pending state");

    Console.WriteLine("M1.4.2: ack tracker smoke tests passed.");
}

static void RunSessionHandshakeSmokeTests()
{
    var (session, conn, listener, flusher) = CreateSession();
    Assert(session.State == ServerSessionState.WaitHello, "initial state must be WaitHello");

    conn.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));

    Assert(session.State == ServerSessionState.WaitAuth, "state must advance to WaitAuth");
    Assert(conn.SentFrames.Count == 1, "server must send WELCOME");
    Assert(conn.SentFrames[0].MessageType == MessageType.Welcome, "first outbound must be WELCOME");
    Assert(conn.SentFrames[0].Sequence == 0, "WELCOME sequence must be 0 (handshake numbering)");
    var welcome = WelcomePayloadCodec.Decode(conn.SentFrames[0].Payload);
    Assert(welcome.AuthRequired, "authRequired must be true (D6)");
    Assert(welcome.SessionId.SequenceEqual(TestData.SessionId), "sessionId must be server-issued");
    Assert(welcome.Challenge.SequenceEqual(TestData.Challenge), "challenge must be server-issued");

    conn.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.TokenAuth("ctrl-42a8", welcome.Challenge)),
        1, mustUnderstand: true));

    Assert(session.State == ServerSessionState.Ready, "state must advance to Ready");
    Assert(conn.SentFrames.Count == 2, "server must send AUTH_OK");
    Assert(conn.SentFrames[1].MessageType == MessageType.AuthOk, "second outbound must be AUTH_OK");
    Assert(conn.SentFrames[1].Sequence == 1, "AUTH_OK sequence must be 1 (handshake numbering)");
    var authOk = AuthOkPayloadCodec.Decode(conn.SentFrames[1].Payload);
    Assert(authOk.SessionId.SequenceEqual(welcome.SessionId), "AUTH_OK must echo sessionId");
    Assert(authOk.NewToken.Length == 0, "token auth must not issue a newToken");

    Console.WriteLine("M1.4.2: session handshake smoke tests passed.");
}

static void RunSessionForbiddenFlagSmokeTests()
{
    var (session, conn, listener, flusher) = CreateSession();
    var raw = MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true);
    raw[4] = 0x02; // reserved flag SECURE — Encode refuses it, so patch raw bytes directly.
    conn.Emit(raw);

    Assert(conn.SentFrames.Count == 1 && conn.SentFrames[0].MessageType == MessageType.Error,
        "reserved flags must produce ERROR");
    var error = ErrorPayloadCodec.Decode(conn.SentFrames[0].Payload);
    Assert(error.Code == ErrorPayloadCodec.CodeForbidden, "reserved flags must map to ERROR forbidden (D2)");
    Assert(session.State == ServerSessionState.Closed, "session must close after forbidden flags");
    Assert(flusher.FlushCount == 1, "flush must run on close");

    Console.WriteLine("M1.4.2: session forbidden-flag smoke tests passed.");
}

static void RunSessionNotAuthenticatedSmokeTests()
{
    var (session, conn, listener, flusher) = CreateSession();
    conn.Emit(MakeFrame(MessageType.InputEvent,
        InputEventPayloadCodec.Encode(new InputEventPayload(new InputEvent("a", 0, 0, State: 1, PressCount: 1))),
        0));

    Assert(conn.SentFrames.Count == 1 && conn.SentFrames[0].MessageType == MessageType.Error,
        "input before auth must produce ERROR");
    Assert(ErrorPayloadCodec.Decode(conn.SentFrames[0].Payload).Code == ErrorPayloadCodec.CodeNotAuthenticated,
        "must be ERROR not-authenticated");
    Assert(session.State == ServerSessionState.Closed, "session must close after not-authenticated");

    Console.WriteLine("M1.4.2: session not-authenticated smoke tests passed.");
}

static void RunSessionInvalidMessageSmokeTests()
{
    // AUTH before HELLO (out of state).
    var (s1, c1, l1, f1) = CreateSession();
    c1.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(new AuthPayload(0x01, "", "ctrl-42a8", new byte[32])),
        0, mustUnderstand: true));
    Assert(c1.SentFrames.Count == 1 && c1.SentFrames[0].MessageType == MessageType.Error,
        "out-of-state AUTH must produce ERROR");
    Assert(ErrorPayloadCodec.Decode(c1.SentFrames[0].Payload).Code == ErrorPayloadCodec.CodeInvalidMessage,
        "out-of-state AUTH must be ERROR invalid-message (D5)");

    // Malformed control payload (HELLO truncated).
    var (s2, c2, l2, f2) = CreateSession();
    c2.Emit(MakeFrame(MessageType.Hello, new byte[] { 0x05 }, 0, mustUnderstand: true));
    Assert(c2.SentFrames.Count == 1 && c2.SentFrames[0].MessageType == MessageType.Error,
        "malformed HELLO must produce ERROR");
    Assert(ErrorPayloadCodec.Decode(c2.SentFrames[0].Payload).Code == ErrorPayloadCodec.CodeInvalidMessage,
        "malformed control payload must be ERROR invalid-message");

    Console.WriteLine("M1.4.2: session invalid-message smoke tests passed.");
}

static void RunSessionUnsupportedMessageSmokeTests()
{
    var (s1, c1, l1, f1) = CreateSession();
    c1.Emit(MakeFrame(0x0E, new byte[] { 0x00 }, 0, mustUnderstand: true));
    Assert(c1.SentFrames.Count == 1 && c1.SentFrames[0].MessageType == MessageType.Error,
        "wajib-dipahami unknown type must produce ERROR");
    Assert(ErrorPayloadCodec.Decode(c1.SentFrames[0].Payload).Code == ErrorPayloadCodec.CodeUnsupportedMessage,
        "wajib-dipahami unknown type must be ERROR unsupported-message");

    var (s2, c2, l2, f2) = CreateSession();
    c2.Emit(MakeFrame(0x20, new byte[] { 0x00 }, 0));
    Assert(c2.SentFrames.Count == 0, "non-wajib unknown type must be ignored");
    Assert(s2.State == ServerSessionState.WaitHello, "ignored message must not close the session");

    Console.WriteLine("M1.4.2: session unsupported-message smoke tests passed.");
}

static void RunSessionWrongDirectionSmokeTests()
{
    var (s1, c1, l1, f1) = CreateSession();
    c1.Emit(MakeFrame(MessageType.Welcome, new byte[16], 0, mustUnderstand: true));
    Assert(c1.SentFrames.Count == 1 && c1.SentFrames[0].MessageType == MessageType.Error,
        "client WELCOME must produce ERROR");
    Assert(ErrorPayloadCodec.Decode(c1.SentFrames[0].Payload).Code == ErrorPayloadCodec.CodeInvalidMessage,
        "wrong-direction known type must be ERROR invalid-message");

    var (s2, c2, l2, f2) = CreateSession();
    c2.Emit(MakeFrame(MessageType.Pong, new byte[16], 0));
    Assert(c2.SentFrames.Count == 1 && c2.SentFrames[0].MessageType == MessageType.Error,
        "client PONG must produce ERROR");
    Assert(ErrorPayloadCodec.Decode(c2.SentFrames[0].Payload).Code == ErrorPayloadCodec.CodeInvalidMessage,
        "client PONG must be ERROR invalid-message");

    Console.WriteLine("M1.4.2: session wrong-direction smoke tests passed.");
}

static void RunSessionInputDropSmokeTests()
{
    var (session, conn, listener, flusher) = HandshakeToReady();
    var sentBefore = conn.SentFrames.Count;

    // Truncated button event: missing pressCount.
    conn.Emit(MakeFrame(MessageType.InputEvent, new byte[] { 0x01, 0x61, 0x00, 0x00, 0x01 }, 2));
    Assert(conn.SentFrames.Count == sentBefore, "malformed input must not produce ERROR");
    Assert(listener.InputEvents.Count == 0, "malformed input must not be delivered");
    Assert(listener.Errors.Any(e => e.Contains("Dropped malformed INPUT_EVENT")), "malformed input must be logged");
    Assert(session.State == ServerSessionState.Ready, "malformed input must not close the session");

    var valid = new InputEvent("btn-fire", InputEventCodec.KindButton, InputEventCodec.FlagStateChanged,
        State: InputEventCodec.StateDown, PressCount: 3);
    conn.Emit(MakeFrame(MessageType.InputEvent, InputEventPayloadCodec.Encode(new InputEventPayload(valid)), 3));
    Assert(listener.InputEvents.Count == 1 && listener.InputEvents[0] == valid, "valid input event must be delivered");

    var snapshot = new InputSnapshotPayload(new List<InputEvent> { valid });
    conn.Emit(MakeFrame(MessageType.InputSnapshot, InputSnapshotPayloadCodec.Encode(snapshot), 4));
    Assert(listener.Snapshots.Count == 1 && listener.Snapshots[0].Events.Count == 1,
        "valid input snapshot must be delivered");

    Console.WriteLine("M1.4.2: session input drop smoke tests passed.");
}

static void RunSessionPongSmokeTests()
{
    var (session, conn, listener, flusher) = HandshakeToReady(now: 5000);
    conn.Emit(MakeFrame(MessageType.Heartbeat, HeartbeatPayloadCodec.Encode(new HeartbeatPayload(42)), 2));

    Assert(conn.SentFrames[^1].MessageType == MessageType.Pong, "server must reply PONG to HEARTBEAT (D7)");
    var pong = PongPayloadCodec.Decode(conn.SentFrames[^1].Payload);
    Assert(pong.ClientSendTime == 42 && pong.ServerTime == 5000, "PONG must echo client send time and carry server time");

    Console.WriteLine("M1.4.2: session PONG smoke tests passed.");
}

static void RunSessionAckSmokeTests()
{
    var (session, conn, listener, flusher) = HandshakeToReady(now: 1000);
    var input = new InputEvent("a", 0, 0, State: 1, PressCount: 1);
    conn.Emit(MakeFrame(MessageType.InputEvent, InputEventPayloadCodec.Encode(new InputEventPayload(input)), 7, ackRequested: true));

    Assert(conn.SentFrames[^1].MessageType == MessageType.Ack, "ACK_REQUESTED must produce ACK");
    var ack = AckPayloadCodec.Decode(conn.SentFrames[^1].Payload);
    Assert(ack.AckedSequence == 7 && ack.AckTime == 1000, "ACK must echo sequence and carry server time");

    Console.WriteLine("M1.4.2: session ACK smoke tests passed.");
}

static void RunSessionAuthDeniedSmokeTests()
{
    // Wrong pairing code: HMAC is self-consistent but the code was never
    // issued -> bad-credential.
    var (s1, c1, l1, f1) = CreateSession();
    c1.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var welcome1 = WelcomePayloadCodec.Decode(c1.SentFrames[0].Payload);

    c1.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.PairingAuth("wrong", "ctrl-42a8", welcome1.Challenge)),
        1, mustUnderstand: true));
    Assert(c1.SentFrames[^1].MessageType == MessageType.AuthDenied, "bad credential must produce AUTH_DENIED");
    Assert(s1.State == ServerSessionState.Closed, "session must close after AUTH_DENIED");
    var denied = AuthDeniedPayloadCodec.Decode(c1.SentFrames[^1].Payload);
    Assert(denied.Reason == AuthDeniedPayloadCodec.ReasonBadCredential, "AUTH_DENIED reason must be bad-credential");

    // Valid pairing code: accepted and issues a 32-byte persistent token.
    var (s2, c2, l2, f2) = CreateSession(authenticator: new HmacAuthenticator(
        AuthTestEnv.MasterKey,
        new InMemoryTokenStore(),
        AuthTestEnv.MakePairingService()));
    c2.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var welcome2 = WelcomePayloadCodec.Decode(c2.SentFrames[0].Payload);

    c2.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.PairingAuth(AuthTestEnv.PairingCode, "ctrl-42a8", welcome2.Challenge)),
        1, mustUnderstand: true));
    Assert(s2.State == ServerSessionState.Ready, "pairing-code auth must succeed");
    var ok = AuthOkPayloadCodec.Decode(c2.SentFrames[^1].Payload);
    Assert(ok.NewToken.Length == 32, "pairing-code auth must issue a 32-byte token");
    Assert(ok.NewToken.SequenceEqual(new HmacAuthenticator(AuthTestEnv.MasterKey).DeriveToken("ctrl-42a8")),
        "issued token must be the deterministic device token");

    Console.WriteLine("M1.4.2: session auth-denied smoke tests passed.");
}

static void RunSessionTakeoverSmokeTests()
{
    var manager = new SessionManager();

    var (s1, c1, l1, f1) = CreateSession();
    manager.Register(s1);
    c1.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var w1 = WelcomePayloadCodec.Decode(c1.SentFrames[0].Payload);
    c1.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.TokenAuth("ctrl-42a8", w1.Challenge)),
        1, mustUnderstand: true));
    Assert(manager.ActiveCount == 1, "first session must be active");

    var (s2, c2, l2, f2) = CreateSession();
    manager.Register(s2);
    c2.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var w2 = WelcomePayloadCodec.Decode(c2.SentFrames[0].Payload);
    c2.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.TokenAuth("ctrl-42a8", w2.Challenge)),
        1, mustUnderstand: true));

    Assert(manager.ActiveCount == 1, "second session must replace the first");
    Assert(manager.IsActive("ctrl-42a8"), "device must remain active");
    Assert(s2.State == ServerSessionState.Ready, "new session must be Ready");
    Assert(s1.State == ServerSessionState.Closed, "old session must be closed (D3)");
    Assert(f1.FlushCount == 1, "old session must flush pending inputs (D3)");
    var last = c1.SentFrames[^1];
    Assert(last.MessageType == MessageType.Error, "old session must receive ERROR");
    Assert(ErrorPayloadCodec.Decode(last.Payload).Code == ErrorPayloadCodec.CodeDeviceLimit,
        "old session must receive ERROR device-limit (D3)");

    Console.WriteLine("M1.4.2: session takeover smoke tests passed.");
}

static async Task RunSessionLoopbackHostSmokeTests()
{
    var server = new TcpServer(0);
    var host = new SessionHost(server, DefaultTestAuthenticator(), new RecordingSessionListener());
    var accepted = new ConcurrentQueue<Session>();
    host.SessionAccepted += s => accepted.Enqueue(s);
    _ = host.StartAsync();
    await WaitUntil(() => host.IsListening);

    var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, server.LocalPort);
    var transport = new TcpConnection(client);
    var frames = new ConcurrentQueue<ProtocolFrame>();
    transport.FrameReceived += f => frames.Enqueue(f);
    var run = transport.RunAsync();

    await transport.SendAsync(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    await WaitUntil(() => frames.Count == 1);
    Assert(frames.First().MessageType == MessageType.Welcome, "host must reply WELCOME");
    var welcome = WelcomePayloadCodec.Decode(frames.First().Payload);

    await transport.SendAsync(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.TokenAuth("ctrl-42a8", welcome.Challenge)),
        1, mustUnderstand: true));
    await WaitUntil(() => frames.Count == 2);
    Assert(frames.Skip(1).First().MessageType == MessageType.AuthOk, "host must reply AUTH_OK");

    await transport.SendAsync(MakeFrame(MessageType.InputEvent,
        InputEventPayloadCodec.Encode(new InputEventPayload(new InputEvent("a", 0, 0, State: 1, PressCount: 1))),
        2));
    await Task.Delay(100);
    Assert(host.Manager.IsActive("ctrl-42a8"), "session must be registered as active");
    Assert(accepted.Count == 1, "SessionHost must raise SessionAccepted");

    await transport.CloseAsync();
    await run;
    await host.StopAsync();

    Console.WriteLine("M1.4.2: loopback SessionHost smoke tests passed.");
}

static void RunSequenceWrapSmokeTests()
{
    var outbound = new SequenceTracker(65534);
    Assert(outbound.Next() == 65534, "wrap seed start");
    Assert(outbound.Next() == 65535, "wrap reaches max");
    Assert(outbound.Next() == 0, "wrap 65535 -> 0");
    Assert(outbound.Next() == 1, "wrap continues after 0");

    var inbound = new InboundSequenceTracker();
    Assert(inbound.IsMonotonic(65534), "wrap first");
    Assert(inbound.IsMonotonic(65535), "wrap second");
    Assert(inbound.IsMonotonic(0), "wrap 65535 -> 0 accepted");
    Assert(inbound.IsMonotonic(1), "wrap continues");

    Console.WriteLine("M1.4.3: sequence wrap smoke tests passed.");
}

static void RunSessionStateTransitionSmokeTests()
{
    var (s1, c1, l1, f1) = CreateSession();
    Assert(s1.State == ServerSessionState.WaitHello, "server starts at WaitHello");
    c1.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    Assert(s1.State == ServerSessionState.WaitAuth, "WaitHello -> WaitAuth");
    c1.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.TokenAuth("ctrl-42a8", TestData.Challenge)),
        1, mustUnderstand: true));
    Assert(s1.State == ServerSessionState.Ready, "WaitAuth -> Ready");
    Assert(l1.States.SequenceEqual(new[]
        {
            ServerSessionState.WaitHello,
            ServerSessionState.WaitAuth,
            ServerSessionState.Ready
        }),
        "listener must observe the exact server state sequence");

    // App-plane INPUT_EVENT between WELCOME and AUTH -> not-authenticated.
    var (s2, c2, l2, f2) = CreateSession();
    c2.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    c2.Emit(MakeFrame(MessageType.InputEvent,
        InputEventPayloadCodec.Encode(new InputEventPayload(new InputEvent("a", 0, 0, State: 1, PressCount: 1))),
        1));
    Assert(c2.SentFrames[^1].MessageType == MessageType.Error,
        "INPUT_EVENT pre-AUTH_OK must produce ERROR");
    Assert(ErrorPayloadCodec.Decode(c2.SentFrames[^1].Payload).Code == ErrorPayloadCodec.CodeNotAuthenticated,
        "INPUT_EVENT pre-AUTH_OK must be ERROR not-authenticated");
    Assert(s2.State == ServerSessionState.Closed, "session must close after pre-auth input");

    // App-plane INPUT_SNAPSHOT before HELLO -> not-authenticated too.
    var (s3, c3, l3, f3) = CreateSession();
    c3.Emit(MakeFrame(MessageType.InputSnapshot,
        InputSnapshotPayloadCodec.Encode(new InputSnapshotPayload(new List<InputEvent>
        {
            new("a", 0, 0, State: 1, PressCount: 1)
        })),
        0));
    Assert(c3.SentFrames[^1].MessageType == MessageType.Error,
        "INPUT_SNAPSHOT pre-AUTH_OK must produce ERROR");
    Assert(ErrorPayloadCodec.Decode(c3.SentFrames[^1].Payload).Code == ErrorPayloadCodec.CodeNotAuthenticated,
        "INPUT_SNAPSHOT pre-AUTH_OK must be ERROR not-authenticated");
    Assert(s3.State == ServerSessionState.Closed, "session must close after pre-auth snapshot");

    Console.WriteLine("M1.4.3: session state-transition smoke tests passed.");
}

static void RunSessionAckLifecycleSmokeTests()
{
    var clock = new MutableClock { Value = 1000 };

    // Normal: server sends an ACK-requested message, client ACKs, no retry.
    var (s1, c1, l1, f1) = CreateSession(nowProvider: clock.Now);
    DoHandshake(s1, c1);
    s1.SendWithAck(MessageType.InputReset, InputResetPayloadCodec.Encode(new InputResetPayload(0)));
    var sent = c1.SentFrames[^1];
    Assert(sent.MessageType == MessageType.InputReset && (sent.Flags & FrameCodec.AckRequested) != 0,
        "SendWithAck must set ACK_REQUESTED");
    var ackSeq = sent.Sequence;
    c1.Emit(MakeFrame(MessageType.Ack, AckPayloadCodec.Encode(new AckPayload(ackSeq, clock.Value)), 100));
    var before1 = c1.SentFrames.Count;
    clock.Value += 3000;
    s1.ProcessPendingAcks();
    Assert(c1.SentFrames.Count == before1, "ACKed sequence must not retransmit");
    Assert(s1.State == ServerSessionState.Ready, "session stays up after ACK");

    // Wrong-sequence ACK: no-op, pending stays and retry still happens.
    var (s2, c2, l2, f2) = CreateSession(nowProvider: clock.Now);
    DoHandshake(s2, c2);
    s2.SendWithAck(MessageType.InputReset, InputResetPayloadCodec.Encode(new InputResetPayload(0)));
    var pendingSeq = c2.SentFrames[^1].Sequence;
    c2.Emit(MakeFrame(MessageType.Ack, AckPayloadCodec.Encode(new AckPayload((ushort)(pendingSeq + 1), clock.Value)), 100));
    var before2 = c2.SentFrames.Count;
    clock.Value += 3000;
    s2.ProcessPendingAcks();
    Assert(c2.SentFrames.Count == before2 + 1, "wrong-seq ACK must not prevent retry");
    Assert(c2.SentFrames[^1].Sequence == pendingSeq, "retransmit must reuse the same sequence");

    // ACK after retry clears pending and stops further retries.
    c2.Emit(MakeFrame(MessageType.Ack, AckPayloadCodec.Encode(new AckPayload(pendingSeq, clock.Value)), 101));
    var before3 = c2.SentFrames.Count;
    clock.Value += 3000;
    s2.ProcessPendingAcks();
    Assert(c2.SentFrames.Count == before3, "ACK after retry must stop further retries");
    Assert(s2.State == ServerSessionState.Ready, "session stays up after ACK-after-retry");

    // Duplicate ACK: harmless.
    var (s5, c5, l5, f5) = CreateSession(nowProvider: clock.Now);
    DoHandshake(s5, c5);
    s5.SendWithAck(MessageType.InputReset, InputResetPayloadCodec.Encode(new InputResetPayload(0)));
    var seq5 = c5.SentFrames[^1].Sequence;
    c5.Emit(MakeFrame(MessageType.Ack, AckPayloadCodec.Encode(new AckPayload(seq5, clock.Value)), 200));
    c5.Emit(MakeFrame(MessageType.Ack, AckPayloadCodec.Encode(new AckPayload(seq5, clock.Value)), 201));
    Assert(s5.State == ServerSessionState.Ready, "duplicate ACK must be harmless");

    // Final timeout after retry: session closes + flush.
    var (s3, c3, l3, f3) = CreateSession(nowProvider: clock.Now);
    DoHandshake(s3, c3);
    s3.SendWithAck(MessageType.InputReset, InputResetPayloadCodec.Encode(new InputResetPayload(0)));
    var seq3 = c3.SentFrames[^1].Sequence;
    clock.Value += 3000;
    s3.ProcessPendingAcks(); // retry once
    Assert(c3.SentFrames[^1].Sequence == seq3, "first retry reuses sequence");
    clock.Value += 3000;
    s3.ProcessPendingAcks(); // final timeout
    Assert(s3.State == ServerSessionState.Closed, "session must close after final ACK timeout");
    Assert(f3.FlushCount == 1, "close after final ACK timeout must flush");

    Console.WriteLine("M1.4.3: session ACK lifecycle smoke tests passed.");
}

static void RunSessionHeartbeatTimeoutSmokeTests()
{
    var clock = new MutableClock { Value = 0 };
    var (s1, c1, l1, f1) = CreateSession(nowProvider: clock.Now);
    DoHandshake(s1, c1); // heartbeat liveness only applies when READY
    Assert(!s1.CheckHeartbeatTimeout(), "no timeout at t=0");

    clock.Value = 2999;
    Assert(!s1.CheckHeartbeatTimeout(), "no timeout before 3000ms");

    // Incoming HEARTBEAT extends liveness.
    c1.Emit(MakeFrame(MessageType.Heartbeat, HeartbeatPayloadCodec.Encode(new HeartbeatPayload(42)), 2));
    Assert(c1.SentFrames[^1].MessageType == MessageType.Pong, "server must reply PONG to HEARTBEAT");
    Assert((c1.SentFrames[^1].Flags & FrameCodec.AckRequested) == 0,
        "PONG must not use ACK_REQUESTED (docs/protocol.md §10)");
    Assert(s1.PendingAckCount == 0, "PONG must not enter the ACK tracker");

    clock.Value = 2999 + 2999;
    Assert(!s1.CheckHeartbeatTimeout(), "heartbeat must extend liveness");
    clock.Value = 5999;
    Assert(s1.CheckHeartbeatTimeout(), "3s without activity must time out");
    Assert(s1.State == ServerSessionState.Closed, "heartbeat timeout must close the session");
    Assert(f1.FlushCount == 1, "heartbeat timeout must flush pending inputs");
    Assert(!s1.CheckHeartbeatTimeout(), "timeout check must be a no-op once closed");

    // Non-READY sessions must not be killed by the heartbeat check.
    var clock2 = new MutableClock { Value = 0 };
    var (s2, c2, l2, f2) = CreateSession(nowProvider: clock2.Now);
    clock2.Value = 10_000;
    Assert(!s2.CheckHeartbeatTimeout(), "handshake sessions must be exempt from heartbeat timeout");

    Console.WriteLine("M1.4.3: session heartbeat-timeout smoke tests passed.");
}

static void RunSessionDisconnectFlushSmokeTests()
{
    // Graceful DISCONNECT.
    var (s1, c1, l1, f1) = CreateSession();
    DoHandshake(s1, c1);
    c1.Emit(MakeFrame(MessageType.Disconnect, DisconnectPayloadCodec.Encode(new DisconnectPayload(0)), 2, mustUnderstand: true));
    Assert(s1.State == ServerSessionState.Closed, "graceful DISCONNECT closes");
    Assert(f1.FlushCount == 1, "graceful DISCONNECT flushes exactly once");

    // Ungraceful TCP close.
    var (s2, c2, l2, f2) = CreateSession();
    DoHandshake(s2, c2);
    c2.EmitDisconnected("peer closed");
    Assert(s2.State == ServerSessionState.Closed, "TCP close closes");
    Assert(f2.FlushCount == 1, "TCP close flushes exactly once");

    // Protocol violation (out-of-state control plane).
    var (s3, c3, l3, f3) = CreateSession();
    DoHandshake(s3, c3);
    c3.Emit(MakeFrame(MessageType.Auth, new byte[] { 0x00 }, 3, mustUnderstand: true));
    Assert(s3.State == ServerSessionState.Closed, "protocol violation closes");
    Assert(f3.FlushCount == 1, "protocol violation flushes exactly once");

    // Auth failure.
    var (s4, c4, l4, f4) = CreateSession();
    c4.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var w4 = WelcomePayloadCodec.Decode(c4.SentFrames[^1].Payload);
    c4.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(new AuthPayload(0x02, "wrong", "ctrl-42a8", w4.Challenge)),
        1, mustUnderstand: true));
    Assert(s4.State == ServerSessionState.Closed, "auth failure closes");
    Assert(f4.FlushCount == 1, "auth failure flushes exactly once");

    // Flush must be idempotent: a late disconnect must not flush again.
    c4.EmitDisconnected("late close");
    Assert(f4.FlushCount == 1, "flush must run exactly once even with a late disconnect");

    Console.WriteLine("M1.4.3: session disconnect-flush smoke tests passed.");
}

static void RunSessionSnapshotBoundarySmokeTests()
{
    var initial = new InputEvent("btn-fire", InputEventCodec.KindButton,
        InputEventCodec.FlagStateChanged | InputEventCodec.FlagInitial,
        State: InputEventCodec.StateDown, PressCount: 5);
    var snapshot = new InputSnapshotPayload(new List<InputEvent> { initial });

    var (s1, c1, l1, f1) = HandshakeToReady();
    c1.Emit(MakeFrame(MessageType.InputSnapshot, InputSnapshotPayloadCodec.Encode(snapshot), 2));
    Assert(l1.Snapshots.Count == 1, "snapshot after AUTH_OK must be delivered");
    Assert((l1.Snapshots[0].Events[0].Flags & InputEventCodec.FlagInitial) != 0,
        "snapshot entry must preserve the initial flag (docs/protocol.md §15)");

    // Reconnect scenario: new session on the same device takes over; the old
    // one is closed + flushed and the new one carries its own snapshot.
    var manager = new SessionManager();
    var tokensRe = new InMemoryTokenStore();
    var trustedRe = new HmacAuthenticator(AuthTestEnv.MasterKey, tokensRe);
    tokensRe.StoreHash("ctrl-re", trustedRe.TokenHash("ctrl-re"));
    var (sA, cA, lA, fA) = CreateSession(authenticator: trustedRe);
    manager.Register(sA);
    DoHandshake(sA, cA, "ctrl-re");
    cA.Emit(MakeFrame(MessageType.InputSnapshot, InputSnapshotPayloadCodec.Encode(snapshot), 2));
    Assert(lA.Snapshots.Count == 1, "first session snapshot delivered");

    var (sB, cB, lB, fB) = CreateSession(authenticator: trustedRe);
    manager.Register(sB);
    DoHandshake(sB, cB, "ctrl-re");
    Assert(sA.State == ServerSessionState.Closed, "reconnect takes over old session (D3)");
    Assert(fA.FlushCount == 1, "reconnect flushes old session inputs");
    cB.Emit(MakeFrame(MessageType.InputSnapshot, InputSnapshotPayloadCodec.Encode(snapshot), 2));
    Assert(lB.Snapshots.Count == 1, "new session snapshot delivered");

    Console.WriteLine("M1.4.3: session snapshot-boundary smoke tests passed.");
}

static void RunSessionSequenceBoundarySmokeTests()
{
    static InputSnapshotPayload EmptySnapshot() =>
        new(new List<InputEvent>
        {
            new("a", InputEventCodec.KindButton, 0, State: 0, PressCount: 0)
        });

    // Outbound: the first server message after AUTH_OK must carry sequence 0
    // (docs/protocol.md §7/§24.5), then increment per message.
    var (s1, c1, l1, f1) = CreateSession();
    DoHandshake(s1, c1);
    c1.Emit(MakeFrame(MessageType.Heartbeat, HeartbeatPayloadCodec.Encode(new HeartbeatPayload(42)), 0));
    Assert(c1.SentFrames[^1].MessageType == MessageType.Pong, "server must answer HEARTBEAT with PONG");
    Assert(c1.SentFrames[^1].Sequence == 0, "post-AUTH_OK outbound sequence must restart at 0");
    c1.Emit(MakeFrame(MessageType.Heartbeat, HeartbeatPayloadCodec.Encode(new HeartbeatPayload(43)), 1));
    Assert(c1.SentFrames[^1].Sequence == 1, "post-AUTH_OK outbound sequence must increment");

    // Inbound: the client restarts at 0 too; the tracker must not flag the
    // AUTH_OK(seq 1) -> snapshot(seq 0) transition as non-monotonic.
    var (s2, c2, l2, f2) = HandshakeToReady();
    c2.Emit(MakeFrame(MessageType.InputSnapshot, InputSnapshotPayloadCodec.Encode(EmptySnapshot()), 0));
    Assert(l2.Snapshots.Count == 1, "snapshot carrying the reset sequence must be delivered");
    Assert(!l2.Errors.Any(e => e.Contains("Non-monotonic")),
        "inbound tracker must be reset at the AUTH_OK boundary");

    // Inbound wrap across 65535 -> 0 is accepted (modulo 2^16).
    var (s3, c3, l3, f3) = HandshakeToReady();
    c3.Emit(MakeFrame(MessageType.InputSnapshot, InputSnapshotPayloadCodec.Encode(EmptySnapshot()), 65535));
    c3.Emit(MakeFrame(MessageType.InputSnapshot, InputSnapshotPayloadCodec.Encode(EmptySnapshot()), 0));
    Assert(l3.Snapshots.Count == 2, "snapshot after wrap must be delivered");
    Assert(!l3.Errors.Any(e => e.Contains("Non-monotonic")),
        "65535 -> 0 must be accepted as monotonic (wrap)");

    // Per-direction independence: inbound numbering does not disturb the
    // outbound counter and vice versa.
    var (s4, c4, l4, f4) = CreateSession();
    DoHandshake(s4, c4);
    c4.Emit(MakeFrame(MessageType.Heartbeat, HeartbeatPayloadCodec.Encode(new HeartbeatPayload(1)), 100));
    c4.Emit(MakeFrame(MessageType.Heartbeat, HeartbeatPayloadCodec.Encode(new HeartbeatPayload(2)), 101));
    Assert(c4.SentFrames[^1].Sequence == 1,
        "outbound counter must be independent of inbound numbering");

    // A fresh session never inherits a previous session's counter.
    var (s5, c5, l5, f5) = CreateSession();
    DoHandshake(s5, c5);
    c5.Emit(MakeFrame(MessageType.Heartbeat, HeartbeatPayloadCodec.Encode(new HeartbeatPayload(9)), 0));
    Assert(c5.SentFrames[^1].Sequence == 0, "a new session starts its counters from scratch");

    Console.WriteLine("M1.4.3: session sequence-boundary smoke tests passed.");
}

static async Task<int> RunIntegrationServerAsync(int port)
{
    var server = new TcpServer(port);
    var manager = new SessionManager();
    var listener = new IntegrationListener();
    var flusher = new IntegrationFlusher();
    // M1.4.4: real §12 authentication. The pairing code is pinned so the Dart
    // integration client can pair deterministically; later phases reconnect
    // with the issued (deterministic) token.
    var pairing = new PairingCodeService(codeFactory: () => AuthTestEnv.PairingCode);
    pairing.IssueExplicit(AuthTestEnv.PairingCode);
    var authenticator = new HmacAuthenticator(
        AuthTestEnv.MasterKey, new InMemoryTokenStore(), pairing);
    var options = new SessionOptions
    {
        SessionIdFactory = () => TestData.SessionId,
        ChallengeFactory = () => TestData.Challenge,
    };

    server.ClientConnected += connection =>
    {
        var recording = new RecordingTransportConnection(connection);
        var session = new Session(
            recording,
            authenticator,
            listener,
            options,
            flusher,
            new IntegrationOutputSink());
        session.Authenticated += s =>
        {
            Console.WriteLine($"C#:AUTHENTICATED:{s.DeviceId}");
            Console.Out.Flush();
        };
        session.Closed += s =>
        {
            Console.WriteLine($"C#:CLOSED:{s.DeviceId}");
            Console.Out.Flush();
        };
        manager.Register(session);
        return Task.CompletedTask;
    };

    _ = server.StartAsync();
    await WaitUntil(() => server.LocalPort >= 0);
    Console.WriteLine($"C#:LISTENING:{server.LocalPort}");
    Console.Out.Flush();

    // Drive the server from stdin: any line other than STOP is ignored; EOF or
    // STOP stops the listener and exits (the Dart integration tool sends STOP).
    while (true)
    {
        var line = Console.ReadLine();
        if (line == null || line.Trim() == "STOP")
            break;
    }

    await server.StopAsync();
    return 0;
}

static byte[] MakeFrame(byte type, byte[] payload, ushort sequence = 0,
    bool ackRequested = false, bool mustUnderstand = false, byte major = 1, ulong timestamp = 0)
    => FrameBuilder.Build(type, payload, sequence, ackRequested, mustUnderstand, major, 0, timestamp);

static (Session, FakeTransportConnection, RecordingSessionListener, RecordingFlusher) CreateSession(
    ulong now = 0, IAuthenticator? authenticator = null, Func<ulong>? nowProvider = null,
    IOutputSink? outputSink = null)
{
    var connection = new FakeTransportConnection();
    var listener = new RecordingSessionListener();
    var flusher = new RecordingFlusher();
    var session = new Session(
        connection,
        authenticator ?? DefaultTestAuthenticator(),
        listener,
        new SessionOptions
        {
            NowMs = nowProvider ?? (() => now),
            SessionIdFactory = () => TestData.SessionId,
            ChallengeFactory = () => TestData.Challenge,
        },
        flusher,
        outputSink);
    return (session, connection, listener, flusher);
}

static WelcomePayload DoHandshake(Session session, FakeTransportConnection conn, string deviceId = "ctrl-42a8")
{
    conn.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload(deviceId, "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var welcome = WelcomePayloadCodec.Decode(conn.SentFrames[^1].Payload);
    conn.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.TokenAuth(deviceId, welcome.Challenge)),
        1, mustUnderstand: true));
    if (session.State != ServerSessionState.Ready)
        throw new Exception("DoHandshake: session did not reach Ready.");
    return welcome;
}

static (Session, FakeTransportConnection, RecordingSessionListener, RecordingFlusher) HandshakeToReady(
    ulong now = 0, string deviceId = "ctrl-42a8", bool pairing = false, string credential = "",
    IAuthenticator? authenticator = null)
{
    var (session, conn, listener, flusher) = CreateSession(now, authenticator);
    conn.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload(deviceId, "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var welcome = WelcomePayloadCodec.Decode(conn.SentFrames[^1].Payload);
    var auth = pairing
        ? AuthTestEnv.PairingAuth(credential, deviceId, welcome.Challenge)
        : AuthTestEnv.TokenAuth(deviceId, welcome.Challenge);
    conn.Emit(MakeFrame(MessageType.Auth, AuthPayloadCodec.Encode(auth), 1, mustUnderstand: true));
    if (session.State != ServerSessionState.Ready)
        throw new Exception("HandshakeToReady: session did not reach Ready.");
    return (session, conn, listener, flusher);
}

static ProtocolFrame MakeEchoFrame(ushort sequence, byte[] payload) => new(
    VersionMajor: 0x01,
    VersionMinor: 0x00,
    Flags: 0,
    MessageType: 0xFF,
    Sequence: sequence,
    Timestamp: 0,
    Payload: payload);

static bool FramesEqual(ProtocolFrame a, ProtocolFrame b) =>
    a.VersionMajor == b.VersionMajor &&
    a.VersionMinor == b.VersionMinor &&
    a.Flags == b.Flags &&
    a.MessageType == b.MessageType &&
    a.Sequence == b.Sequence &&
    a.Timestamp == b.Timestamp &&
    a.Payload.SequenceEqual(b.Payload);

static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
{
    var deadline = Environment.TickCount64 + timeoutMs;
    while (!condition())
    {
        if (Environment.TickCount64 > deadline)
            throw new Exception("Timed out waiting for condition.");
        await Task.Delay(10);
    }
}

static void Assert(bool condition, string name)
{
    if (!condition)
        throw new Exception($"Assertion failed: {name}.");
}

/// <summary>Default authenticator for session suites: real HMAC with a fresh
/// store pre-paired for ctrl-42a8.</summary>
static IAuthenticator DefaultTestAuthenticator(string deviceId = "ctrl-42a8")
{
    var tokens = new InMemoryTokenStore();
    var auth = new HmacAuthenticator(AuthTestEnv.MasterKey, tokens);
    tokens.StoreHash(deviceId, auth.TokenHash(deviceId));
    return auth;
}

// ---------------------------------------------------------------------------
// M1.4.4 — real authentication (docs/protocol.md §12)
// ---------------------------------------------------------------------------

static void RunHmacVectorSmokeTests()
{
    // RFC 4231 test cases pinned as literals so the implementation is checked
    // against the specification, not just against .NET's own HMAC class.
    var rfc1 = AuthTestEnv.Hmac(
        Enumerable.Repeat((byte)0x0b, 20).ToArray(),
        Encoding.UTF8.GetBytes("Hi There"));
    Assert(AuthTestEnv.Hex(rfc1) ==
        "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7",
        "RFC 4231 case 1 vector");

    var rfc2 = AuthTestEnv.Hmac(
        Encoding.UTF8.GetBytes("Jefe"),
        Encoding.UTF8.GetBytes("what do ya want for nothing?"));
    Assert(AuthTestEnv.Hex(rfc2) ==
        "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843",
        "RFC 4231 case 2 vector");

    // Cross-language vectors: the Dart suite pins these exact literals too.
    var challenge = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    var cross = AuthTestEnv.Hmac(
        Encoding.UTF8.GetBytes("ctrl-m144-cross-vector-secret"), challenge);
    Assert(AuthTestEnv.Hex(cross) ==
        "ce7542e18060a6367f4b393b7203b929bc5b2875d0a17f0be67e71a49210a23f",
        "cross-language pairing-secret vector");
    var tokenVector = AuthTestEnv.Hmac(
        Encoding.UTF8.GetBytes("ctrl-m144-token-secret"), challenge);
    Assert(AuthTestEnv.Hex(tokenVector) ==
        "6a074159523a97c4e184ee965814fe428ece623530925f54a5ea6194bdd18945",
        "cross-language token-secret vector");
    Assert(cross.Length == 32 && tokenVector.Length == 32,
        "challengeResponse must stay exactly 32 bytes");

    // Authenticator-level negative path: a modified challenge is rejected.
    var tokensNeg = new InMemoryTokenStore();
    var authNeg = new HmacAuthenticator(AuthTestEnv.MasterKey, tokensNeg);
    tokensNeg.StoreHash("neg-1", authNeg.TokenHash("neg-1"));
    var (sN, cN, lN, fN) = CreateSession(authenticator: authNeg);
    cN.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("neg-1", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var wN = WelcomePayloadCodec.Decode(cN.SentFrames[^1].Payload);
    var mutated = (byte[])wN.Challenge.Clone();
    mutated[0] ^= 0xFF;
    cN.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(new AuthPayload(
            AuthPayloadCodec.CredentialTypeToken, "", "neg-1",
            HmacAuthenticator.HmacSha256(AuthTestEnv.TokenFor("neg-1"), mutated))),
        1, mustUnderstand: true));
    Assert(sN.State == ServerSessionState.Closed, "modified challenge must close the session");
    Assert(cN.SentFrames[^1].MessageType == MessageType.AuthDenied &&
        AuthDeniedPayloadCodec.Decode(cN.SentFrames[^1].Payload).Reason ==
            AuthDeniedPayloadCodec.ReasonBadCredential,
        "HMAC over a modified challenge must be rejected");

    // A token-auth attempt for an unpaired device is rejected even with a
    // self-consistent HMAC over that device's derived token.
    var (sU, cU, lU, fU) = CreateSession();
    cU.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("stranger", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var wU = WelcomePayloadCodec.Decode(cU.SentFrames[^1].Payload);
    cU.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.TokenAuth("stranger", wU.Challenge)),
        1, mustUnderstand: true));
    Assert(cU.SentFrames[^1].MessageType == MessageType.AuthDenied &&
        AuthDeniedPayloadCodec.Decode(cU.SentFrames[^1].Payload).Reason ==
            AuthDeniedPayloadCodec.ReasonBadCredential,
        "unknown device token must be rejected");

    Console.WriteLine("M1.4.4: HMAC vector smoke tests passed.");
}

static void RunRealAuthTokenLifecycleSmokeTests()
{
    var clock = new MutableClock { Value = 1000 };
    var pairing = new PairingCodeService(nowMs: clock.Now);
    pairing.IssueExplicit(AuthTestEnv.PairingCode);
    var tokens = new InMemoryTokenStore();
    var auth = new HmacAuthenticator(AuthTestEnv.MasterKey, tokens, pairing);

    // Pairing success stores a HASH of the token — never the raw bytes.
    var (sA, cA, lA, fA) = HandshakeToReady(authenticator: auth, pairing: true,
        credential: AuthTestEnv.PairingCode);
    var okA = AuthOkPayloadCodec.Decode(cA.SentFrames[^1].Payload);
    Assert(okA.NewToken.Length == 32, "pairing must issue a 32-byte token");
    Assert(tokens.TryGetHash("ctrl-42a8", out var storedHash), "token record must exist after pairing");
    var rawToken = auth.DeriveToken("ctrl-42a8");
    Assert(!storedHash.SequenceEqual(rawToken), "store must not contain the raw token");
    Assert(storedHash.SequenceEqual(HmacAuthenticator.Sha256(rawToken)),
        "store must contain SHA-256(token)");
    Assert(okA.NewToken.SequenceEqual(rawToken), "issued token must equal the deterministic token");

    // The issued token authenticates later reconnects; AUTH_OK has no newToken.
    var (sB, cB, lB, fB) = HandshakeToReady(authenticator: auth);
    var okB = AuthOkPayloadCodec.Decode(cB.SentFrames[^1].Payload);
    Assert(okB.NewToken.Length == 0, "token reconnect must not issue a newToken");

    // An invalid token (tampered response) is denied and counts as a failure.
    var (sC, cC, lC, fC) = CreateSession(authenticator: auth);
    cC.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var wC = WelcomePayloadCodec.Decode(cC.SentFrames[^1].Payload);
    var tampered = AuthTestEnv.TokenAuth("ctrl-42a8", wC.Challenge);
    tampered.ChallengeResponse[31] ^= 0x01;
    cC.Emit(MakeFrame(MessageType.Auth, AuthPayloadCodec.Encode(tampered), 1, mustUnderstand: true));
    Assert(cC.SentFrames[^1].MessageType == MessageType.AuthDenied,
        "tampered token response must be denied");
    Assert(sC.State == ServerSessionState.Closed, "denied session closes");

    // After the failure the valid token still works (success resets the counter).
    var (sD, _, _, _) = HandshakeToReady(authenticator: auth);
    Assert(sD.State == ServerSessionState.Ready, "valid token works after a failure");

    Console.WriteLine("M1.4.4: real-auth token lifecycle smoke tests passed.");
}

static void RunPairingLifecycleSmokeTests()
{
    var clock = new MutableClock { Value = 10_000 };
    var pairing = new PairingCodeService(nowMs: clock.Now);
    var auth = new HmacAuthenticator(AuthTestEnv.MasterKey, new InMemoryTokenStore(), pairing);

    // Valid code accepted exactly once.
    pairing.IssueExplicit("111111");
    var (sOk, _, _, _) = HandshakeToReady(authenticator: auth, pairing: true, credential: "111111");
    Assert(sOk.State == ServerSessionState.Ready, "valid pairing code must be accepted");

    // Reuse of a consumed code is denied (single-use).
    var (sReuse, cReuse, lReuse, fReuse) = CreateSession(authenticator: auth);
    cReuse.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var wReuse = WelcomePayloadCodec.Decode(cReuse.SentFrames[^1].Payload);
    cReuse.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.PairingAuth("111111", "ctrl-42a8", wReuse.Challenge)),
        1, mustUnderstand: true));
    Assert(cReuse.SentFrames[^1].MessageType == MessageType.AuthDenied &&
        AuthDeniedPayloadCodec.Decode(cReuse.SentFrames[^1].Payload).Reason ==
            AuthDeniedPayloadCodec.ReasonBadCredential,
        "consumed pairing code must be rejected");

    // TTL boundary: still valid one tick before expiry.
    clock.Value = 20_000;
    pairing.IssueExplicit("222222"); // expires at 320_000
    clock.Value = 319_999;
    var (sTtl, _, _, _) = HandshakeToReady(authenticator: auth, pairing: true, credential: "222222");
    Assert(sTtl.State == ServerSessionState.Ready, "code must be usable one tick before expiry");

    // Expired exactly at expiresAt -> expired-code.
    clock.Value = 30_000;
    pairing.IssueExplicit("333333"); // expires at 330_000
    clock.Value = 330_000;
    var (sExp, cExp, lExp, fExp) = CreateSession(authenticator: auth);
    cExp.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var wExp = WelcomePayloadCodec.Decode(cExp.SentFrames[^1].Payload);
    cExp.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.PairingAuth("333333", "ctrl-42a8", wExp.Challenge)),
        1, mustUnderstand: true));
    Assert(cExp.SentFrames[^1].MessageType == MessageType.AuthDenied &&
        AuthDeniedPayloadCodec.Decode(cExp.SentFrames[^1].Payload).Reason ==
            AuthDeniedPayloadCodec.ReasonExpiredCode,
        "expired pairing code must yield expired-code");

    // Unknown code never issued -> bad-credential.
    var (sUnk, cUnk, lUnk, fUnk) = CreateSession(authenticator: auth);
    cUnk.Emit(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    var wUnk = WelcomePayloadCodec.Decode(cUnk.SentFrames[^1].Payload);
    cUnk.Emit(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.PairingAuth("999999", "ctrl-42a8", wUnk.Challenge)),
        1, mustUnderstand: true));
    Assert(cUnk.SentFrames[^1].MessageType == MessageType.AuthDenied &&
        AuthDeniedPayloadCodec.Decode(cUnk.SentFrames[^1].Payload).Reason ==
            AuthDeniedPayloadCodec.ReasonBadCredential,
        "unknown pairing code must yield bad-credential");

    Console.WriteLine("M1.4.4: pairing lifecycle smoke tests passed.");
}

static void RunLockoutSmokeTests()
{
    const string ip = "203.0.113.9";
    var clock = new MutableClock { Value = 50_000 };
    var limiter = new AuthRateLimiter(maxFailures: 5, lockoutMs: 30_000, nowMs: clock.Now);
    var pairing = new PairingCodeService(nowMs: clock.Now);
    var auth = new HmacAuthenticator(AuthTestEnv.MasterKey, new InMemoryTokenStore(), pairing, limiter);

    AuthResult Attempt(Func<string, byte[], AuthPayload> buildAuth)
    {
        var (_, conn, _, _) = CreateSession(authenticator: auth);
        conn.RemoteAddress = ip;
        conn.Emit(MakeFrame(MessageType.Hello,
            HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
            0, mustUnderstand: true));
        var welcome = WelcomePayloadCodec.Decode(conn.SentFrames[^1].Payload);
        return auth.Authenticate(buildAuth("ctrl-42a8", welcome.Challenge), welcome.Challenge, ip);
    }

    static AuthPayload BadPairing(string deviceId, byte[] challenge) =>
        AuthTestEnv.PairingAuth("000001", deviceId, challenge); // consistent HMAC, unknown code

    // Four failures: still allowed to try again.
    for (var i = 0; i < 4; i++)
    {
        var result = Attempt(BadPairing);
        Assert(!result.Accepted, $"failure {i + 1} must be denied");
        Assert(!limiter.IsLocked(ip, "ctrl-42a8"), "lockout must not trigger before 5 failures");
    }

    // Fifth failure locks the key.
    var fifth = Attempt(BadPairing);
    Assert(!fifth.Accepted, "fifth failure must be denied");
    Assert(limiter.IsLocked(ip, "ctrl-42a8"), "five failures must lock the key for 30 s");

    // During the lockout window even VALID credentials are rejected...
    clock.Value += 29_999;
    pairing.IssueExplicit("777777");
    var lockedValid = Attempt((deviceId, challenge) =>
        AuthTestEnv.PairingAuth("777777", deviceId, challenge));
    Assert(!lockedValid.Accepted, "valid credentials during lockout must be rejected");

    // ...and the counter is per IP+deviceId: another address/device is free.
    Assert(!limiter.IsLocked("198.51.100.7", "ctrl-42a8"), "other IPs must not inherit the lock");
    Assert(!limiter.IsLocked(ip, "other-device"), "other devices must not inherit the lock");

    // After the window a valid authentication succeeds again.
    clock.Value += 1;
    pairing.IssueExplicit("666666");
    var recovered = Attempt((deviceId, challenge) =>
        AuthTestEnv.PairingAuth("666666", deviceId, challenge));
    Assert(recovered.Accepted, "after the 30 s window authentication works again");
    Assert(!limiter.IsLocked(ip, "ctrl-42a8"), "success must clear the lockout state");

    Console.WriteLine("M1.4.4: lockout smoke tests passed.");
}

// ---------------------------------------------------------------------------
// M2.0 — input architecture (Session → IOutputSink boundary)
// ---------------------------------------------------------------------------

static void RunOutputSinkSmokeTests()
{
    static InputEvent Key(string id, byte state, byte flags) =>
        new(id, InputEventCodec.KindButton, flags, State: state, PressCount: 1);

    // Valid INPUT_EVENT reaches the sink with identical fields.
    var sink = new TestInputSink();
    var (s1, c1, l1, f1) = CreateSession(outputSink: sink);
    DoHandshake(s1, c1);
    var sent = Key("key:65", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged);
    c1.Emit(MakeFrame(MessageType.InputEvent, InputEventPayloadCodec.Encode(new InputEventPayload(sent)), 2));
    Assert(l1.InputEvents.Count == 1, "listener must still receive input");
    Assert(sink.Inputs.Count == 1 && sink.Inputs[0] == sent,
        "session must forward decoded INPUT_EVENT to IOutputSink unchanged");

    // INPUT_SNAPSHOT reaches the sink too.
    var snapshot = new InputSnapshotPayload(new List<InputEvent>
    {
        Key("key:65", InputEventCodec.StateDown,
            InputEventCodec.FlagStateChanged | InputEventCodec.FlagInitial)
    });
    c1.Emit(MakeFrame(MessageType.InputSnapshot, InputSnapshotPayloadCodec.Encode(snapshot), 3));
    Assert(sink.Snapshots.Count == 1 && sink.Snapshots[0].Events[0].ControlId == "key:65",
        "session must forward INPUT_SNAPSHOT to IOutputSink");

    // Malformed payloads must NOT reach the sink.
    var before = sink.Inputs.Count;
    c1.Emit(MakeFrame(MessageType.InputEvent, new byte[] { 0x00 }, 4));
    Assert(sink.Inputs.Count == before, "malformed input must not reach the sink");

    // Graceful DISCONNECT releases everything exactly once.
    c1.Emit(MakeFrame(MessageType.Disconnect, DisconnectPayloadCodec.Encode(new DisconnectPayload(0)), 5, mustUnderstand: true));
    Assert(sink.ReleaseAllCount == 1, "graceful disconnect must ReleaseAll once");

    // Ungraceful close also releases.
    var sink2 = new TestInputSink();
    var (s2, c2, l2, f2) = CreateSession(outputSink: sink2);
    DoHandshake(s2, c2);
    c2.EmitDisconnected("peer dropped");
    Assert(sink2.ReleaseAllCount == 1, "ungraceful close must ReleaseAll once");

    // Takeover closes the old session and releases its outputs.
    var manager = new SessionManager();
    var sinkOld = new TestInputSink();
    var sinkNew = new TestInputSink();
    var authShared = DefaultTestAuthenticator();
    var (sA, cA, lA, fA) = CreateSession(authenticator: authShared, outputSink: sinkOld);
    manager.Register(sA);
    DoHandshake(sA, cA);
    var (sB, cB, lB, fB) = CreateSession(authenticator: authShared, outputSink: sinkNew);
    manager.Register(sB);
    DoHandshake(sB, cB);
    Assert(sA.State == ServerSessionState.Closed, "old session taken over");
    Assert(sinkOld.ReleaseAllCount == 1 && sinkNew.ReleaseAllCount == 0,
        "takeover must release only the displaced session's outputs");

    // A throwing sink never breaks the session or the hot path.
    var bad = new TestInputSink { ThrowOnHandle = true };
    var (sT, cT, lT, fT) = CreateSession(outputSink: bad);
    DoHandshake(sT, cT);
    cT.Emit(MakeFrame(MessageType.InputEvent,
        InputEventPayloadCodec.Encode(new InputEventPayload(
            Key("key:66", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged))), 2));
    Assert(sT.State == ServerSessionState.Ready, "sink failure must not take the session down");
    Assert(lT.Errors.Any(e => e.Contains("HandleInput failed")),
        "sink failure must be reported via listener");
    bad.ThrowOnHandle = false;
    cT.Emit(MakeFrame(MessageType.Disconnect, DisconnectPayloadCodec.Encode(new DisconnectPayload(0)), 3, mustUnderstand: true));
    Assert(sink_ReleaseAllRan(bad), "ReleaseAll still runs after earlier HandleInput failures");

    Console.WriteLine("M2.0: output-sink smoke tests passed.");
}

static bool sink_ReleaseAllRan(TestInputSink sink) => sink.ReleaseAllCount >= 1;

static void RunWin32MapperSmokeTests()
{
    static InputEvent Button(string id, byte state, byte flags) =>
        new(id, InputEventCodec.KindButton, flags, State: state, PressCount: 1);

    // Decimal + hex virtual-key controlIds map to keyboard down/up.
    var down = Win32InputMapper.Map(Button("key:65", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged));
    Assert(down.Kind == Win32OutputKind.KeyDown && down.VirtualKey == 65, "key:65 down maps to VK 65 down");
    var upHex = Win32InputMapper.Map(Button("key:0x41", InputEventCodec.StateUp, InputEventCodec.FlagStateChanged));
    Assert(upHex.Kind == Win32OutputKind.KeyUp && upHex.VirtualKey == 0x41, "key:0x41 up maps to VK 0x41 up");

    // Snapshot initial events press keys too.
    var initial = Win32InputMapper.Map(Button("key:0x1D", InputEventCodec.StateDown, InputEventCodec.FlagInitial));
    Assert(initial.Kind == Win32OutputKind.KeyDown && initial.VirtualKey == 0x1D,
        "initial-flag button maps as a key press");

    // Non-mapping cases are explicit None values.
    Assert(Win32InputMapper.Map(Button("btn-fire", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged)).Kind
        == Win32OutputKind.None, "non-key controlId maps to None in M2.0");
    Assert(Win32InputMapper.Map(Button("key:", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged)).Kind
        == Win32OutputKind.None, "empty key value maps to None");
    Assert(Win32InputMapper.Map(Button("key:0xFFFF+1", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged)).Kind
        == Win32OutputKind.None, "invalid key value maps to None");
    Assert(Win32InputMapper.Map(Button("key:300", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged)).Kind
        == Win32OutputKind.None, "out-of-range VK maps to None");
    var axis = new InputEvent("stick", InputEventCodec.KindStick, InputEventCodec.FlagStateChanged, X: 0.5f, Y: -0.5f);
    Assert(Win32InputMapper.Map(axis).Kind == Win32OutputKind.None,
        "axis events are outside M2.0 scope and map to None");

    // --- M2.1: named keys, modifiers, extended keys, combinations -----------
    Assert(Win32InputMapper.Map(Button("key:A", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged))
        is { Kind: Win32OutputKind.KeyDown, VirtualKey: 65, Extended: false },
        "named letter key maps to its ASCII VK");
    Assert(Win32InputMapper.Map(Button("key:f5", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged))
        is { Kind: Win32OutputKind.KeyDown, VirtualKey: 0x74 },
        "function-key names are case-insensitive");
    Assert(Win32InputMapper.Map(Button("KEY:LCONTROL", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged))
        is { Kind: Win32OutputKind.KeyDown, VirtualKey: 0xA2 },
        "modifier names resolve (case-insensitive)");

    var left = Win32InputMapper.Map(Button("key:LEFT", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged));
    Assert(left is { Kind: Win32OutputKind.KeyDown, VirtualKey: 0x25, Extended: true },
        "arrow keys require the extended-key flag");

    var rctrlUp = Win32InputMapper.Map(Button("key:RCONTROL", InputEventCodec.StateUp, InputEventCodec.FlagStateChanged));
    Assert(rctrlUp is { Kind: Win32OutputKind.KeyUp, VirtualKey: 0xA3, Extended: true },
        "right control maps as an extended key-up");

    // Combination flow through the sink: LCONTROL down, C down, C up, LCONTROL
    // up — held set tracks the chord, ReleaseAll clears the remainder.
    var sent = new List<Win32Output>();
    var chordSink = new Win32InputSink(actions => { sent.AddRange(actions); return actions.Count; });
    chordSink.HandleInput(Button("key:RCONTROL", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged));
    chordSink.HandleInput(Button("key:C", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged));
    Assert(new HashSet<ushort>(chordSink.HeldKeys).SetEquals(new[] { (ushort)0xA3, (ushort)0x43 }),
        "combination holds both modifier and key");
    chordSink.HandleInput(Button("key:C", InputEventCodec.StateUp, InputEventCodec.FlagStateChanged));
    Assert(new HashSet<ushort>(chordSink.HeldKeys).SetEquals(new[] { (ushort)0xA3 }),
        "releasing one key keeps the modifier held");
    chordSink.ReleaseAll();
    Assert(chordSink.HeldKeys.Count == 0, "emergency release empties all held keys");
    Assert(sent.Count(a => a.Kind == Win32OutputKind.KeyDown) == 2 &&
           sent.Count(a => a.Kind == Win32OutputKind.KeyUp) == 2,
        "every down/up reaches SendInput exactly once (no duplicate sends)");
    Assert(sent.Any(a => a is { Kind: Win32OutputKind.KeyUp, VirtualKey: 0xA3, Extended: true }),
        "release path preserves the extended flag for RCTRL-class keys");

    // Emergency release also works when only modifiers are stuck.
    chordSink.HandleInput(Button("key:LSHIFT", InputEventCodec.StateDown, InputEventCodec.FlagInitial));
    Assert(chordSink.HeldKeys.Count == 1, "modifier re-hold registers");
    chordSink.ReleaseAll();
    Assert(chordSink.HeldKeys.Count == 0, "stuck modifier released by emergency release");

    // Unknown names stay None.
    Assert(Win32InputMapper.Map(Button("key:COFFEE", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged)).Kind
        == Win32OutputKind.None, "unknown key name maps to None");

    // Sink lifecycle over the injected send path: hold two keys, release one,
    // then ReleaseAll frees exactly what remains — no real SendInput in tests.
    ushort? lastReleased = null;
    var sentActions = new List<Win32Output>();
    var sink = new Win32InputSink(actions =>
    {
        sentActions.AddRange(actions);
        return actions.Count;
    });
    sink.HandleInput(Button("key:65", InputEventCodec.StateDown, InputEventCodec.FlagStateChanged));
    sink.HandleSnapshot(new InputSnapshotPayload(new List<InputEvent>
    {
        Button("key:66", InputEventCodec.StateDown, InputEventCodec.FlagInitial),
        Button("key:67", InputEventCodec.StateUp, InputEventCodec.FlagInitial),
    }));
    // M2.3: INPUT_SNAPSHOT is authoritative (§15) — key 65 is not in the
    // snapshot, so reconciliation releases it while pressing 66.
    Assert(sentActions.Any(a => a is { Kind: Win32OutputKind.KeyDown, VirtualKey: 66 }),
        "snapshot presses the listed key");
    Assert(sentActions.Any(a => a is { Kind: Win32OutputKind.KeyUp, VirtualKey: 65 }),
        "snapshot auto-releases keys it omits");
    Assert(new HashSet<ushort>(sink.HeldKeys).SetEquals(new[] { (ushort)66 }),
        "held-key state matches the snapshot exactly");
    var beforeRelease = sentActions.Count;
    sink.ReleaseAll();
    Assert(sink.HeldKeys.Count == 0, "ReleaseAll must empty held keys");
    var releasedKeys = sentActions.Skip(beforeRelease)
        .Where(a => a.Kind == Win32OutputKind.KeyUp).Select(a => a.VirtualKey).ToHashSet();
    Assert(releasedKeys.SetEquals(new HashSet<ushort> { 66 }),
        "ReleaseAll must release exactly the held keys");
    sink.ReleaseAll();
    Assert(sentActions.Count == beforeRelease + 1, "ReleaseAll must be idempotent when idle");

    Console.WriteLine("M2.0: win32 mapper smoke tests passed.");
}

// ---------------------------------------------------------------------------
// M2.2 — mouse input (buttons, relative motion loop, wheel)
// ---------------------------------------------------------------------------

static void RunMouseSinkSmokeTests()
{
    static InputEvent MouseButton(string id, byte state, byte flags = InputEventCodec.FlagStateChanged) =>
        new(id, InputEventCodec.KindButton, flags, State: state, PressCount: 1);

    // --- Mapper: buttons ---------------------------------------------------
    var leftDown = Win32InputMapper.Map(MouseButton("mouse:left", InputEventCodec.StateDown));
    Assert(leftDown is { Kind: Win32OutputKind.MouseDown, MouseButton: Win32MouseButton.Left },
        "mouse:left down maps to Left MouseDown");
    var rightUp = Win32InputMapper.Map(MouseButton("mouse:right", InputEventCodec.StateUp));
    Assert(rightUp is { Kind: Win32OutputKind.MouseUp, MouseButton: Win32MouseButton.Right },
        "mouse:right up maps to Right MouseUp");
    var middleDown = Win32InputMapper.Map(
        MouseButton("mouse:middle", InputEventCodec.StateDown, InputEventCodec.FlagInitial));
    Assert(middleDown is { Kind: Win32OutputKind.MouseDown, MouseButton: Win32MouseButton.Middle },
        "initial-flag middle down maps as a press");

    // Keyboard convention still wins for key: controlIds.
    Assert(Win32InputMapper.Map(MouseButton("key:65", InputEventCodec.StateDown)).Kind
        == Win32OutputKind.KeyDown, "keyboard mapping unaffected by mouse additions");

    // --- Mapper: relative movement + wheel ---------------------------------
    var move = Win32InputMapper.Map(new InputEvent(
        "mouse:move", InputEventCodec.KindStick, 0, X: -0.5f, Y: 1.5f));
    Assert(move is { Kind: Win32OutputKind.MoveVelocity, Fx: -0.5f }
        && move.Fy <= 1.0f && move.Fy >= -1.0f,
        "stick mouse:move maps to clamped velocity");

    var wheelUp = Win32InputMapper.Map(new InputEvent(
        "mouse:wheelup", InputEventCodec.KindAxis, InputEventCodec.FlagStateChanged, Value: 0.75f));
    Assert(wheelUp is { Kind: Win32OutputKind.WheelVelocity, VirtualKey: Win32InputMapper.WheelDirectionUp }
        && wheelUp.Fx > 0.74f && wheelUp.Fx < 0.76f,
        "wheelup axis maps to up-direction velocity");

    var gamepadStick = Win32InputMapper.Map(new InputEvent(
        "look", InputEventCodec.KindStick, 0, X: 0.9f, Y: 0.9f));
    Assert(gamepadStick.Kind == Win32OutputKind.None,
        "non-mouse stick maps to None");

    // --- Sink: held buttons + ReleaseAll ------------------------------------
    var sent = new List<Win32Output>();
    var sink = new Win32InputSink(actions => { sent.AddRange(actions); return actions.Count; }, motionLoopEnabled: false);
    sink.HandleInput(MouseButton("mouse:left", InputEventCodec.StateDown));
    sink.HandleInput(MouseButton("mouse:left", InputEventCodec.StateDown)); // duplicate ignored
    sink.HandleInput(MouseButton("mouse:right", InputEventCodec.StateDown));
    Assert(sink.HeldButtons.Count == 2 &&
           sink.HeldButtons.Contains(Win32MouseButton.Left) &&
           sink.HeldButtons.Contains(Win32MouseButton.Right),
        "held-button tracking records distinct buttons");
    sink.HandleInput(MouseButton("mouse:left", InputEventCodec.StateUp));
    Assert(sink.HeldButtons.Count == 1 &&
           sent.Count(a => a is { Kind: Win32OutputKind.MouseUp, MouseButton: Win32MouseButton.Left }) == 1,
        "button up releases exactly one held button");
    var beforeReleaseAll = sent.Count;
    sink.ReleaseAll();
    Assert(sink.HeldButtons.Count == 0,
        "ReleaseAll releases remaining mouse buttons");
    Assert(sent.Skip(beforeReleaseAll)
               .Any(a => a is { Kind: Win32OutputKind.MouseUp, MouseButton: Win32MouseButton.Right }),
        "ReleaseAll sent the right-button up");
    sink.ReleaseAll();
    Assert(sent.Count == beforeReleaseAll + 1, "ReleaseAll idempotent when idle");

    // --- Sink: deterministic motion loop -------------------------------------
    var motionSent = new List<Win32Output>();
    var motionSink = new Win32InputSink(actions => { motionSent.AddRange(actions); return actions.Count; }, motionLoopEnabled: false);
    motionSink.HandleInput(new InputEvent(
        "mouse:move", InputEventCodec.KindStick, InputEventCodec.FlagStateChanged, X: 1.0f, Y: 0.0f));
    motionSink.PumpTick();
    var moveActions = motionSent.Where(a => a.Kind == Win32OutputKind.MoveVelocity).ToList();
    Assert(moveActions.Count == 1 && moveActions[0].Fx > 0,
        "full-deflection stick produces rightward cursor delta");
    // Center the stick: motion stops.
    motionSink.HandleInput(new InputEvent(
        "mouse:move", InputEventCodec.KindStick, InputEventCodec.FlagStateChanged, X: 0.0f, Y: 0.0f));
    var countAfterCenter = motionSent.Count;
    motionSink.PumpTick();
    motionSink.PumpTick();
    Assert(motionSent.Count == countAfterCenter,
        "centered stick stops cursor motion");
    // ReleaseAll clears any residual motion state.
    motionSink.HandleInput(new InputEvent(
        "mouse:move", InputEventCodec.KindStick, InputEventCodec.FlagStateChanged, X: 1.0f, Y: 0.0f));
    motionSink.ReleaseAll();
    var countAfterRelease = motionSent.Count;
    motionSink.PumpTick();
    Assert(motionSent.Count == countAfterRelease,
        "ReleaseAll clears motion velocity (§16)");

    // --- Wheel accumulation ---------------------------------------------------
    var wheelSent = new List<Win32Output>();
    var wheelSink = new Win32InputSink(actions => { wheelSent.AddRange(actions); return actions.Count; }, motionLoopEnabled: false);
    wheelSink.HandleInput(new InputEvent(
        "mouse:wheelup", InputEventCodec.KindAxis, InputEventCodec.FlagStateChanged, Value: 1.0f));
    for (var i = 0; i < 11; i++)
        wheelSink.PumpTick(); // 6 notches/sec @16ms -> ~0.096/tick; 11 ticks >= 1 notch
    var ups = wheelSent.Count(a => a.Kind == Win32OutputKind.WheelVelocity
        && a.VirtualKey == Win32InputMapper.WheelDirectionUp);
    Assert(ups >= 1, "sustained wheelup produces at least one notch");
    Assert(ups <= 2, "notch rate stays bounded (no runaway accumulation)");
    wheelSink.ReleaseAll();
    var upsAfterRelease = wheelSent.Count(a => a.Kind == Win32OutputKind.WheelVelocity);
    for (var i = 0; i < 20; i++)
        wheelSink.PumpTick();
    Assert(wheelSent.Count(a => a.Kind == Win32OutputKind.WheelVelocity) == upsAfterRelease,
        "ReleaseAll clears wheel accumulators");

    // --- Session integration: snapshot with initial mouse press reaches sink --
    var sessionSink = new TestInputSink();
    var (sM, cM, lM, fM) = CreateSession(outputSink: sessionSink);
    DoHandshake(sM, cM);
    cM.Emit(MakeFrame(MessageType.InputSnapshot, InputSnapshotPayloadCodec.Encode(
        new InputSnapshotPayload(new List<InputEvent>
        {
            MouseButton("mouse:left", InputEventCodec.StateDown,
                InputEventCodec.FlagStateChanged | InputEventCodec.FlagInitial),
        })), 2));
    Assert(sessionSink.Snapshots.Count == 1 &&
           sessionSink.Snapshots[0].Events[0].ControlId == "mouse:left",
        "snapshot carrying a mouse button reaches the output sink");

    Console.WriteLine("M2.2: mouse input smoke tests passed.");
}

// ---------------------------------------------------------------------------
// M2.3 — input E2E / reliability
// ---------------------------------------------------------------------------

static async Task RunInputReliabilitySmokeTests()
{
    static InputEvent KeyButton(string id, byte state, byte flags = InputEventCodec.FlagStateChanged) =>
        new(id, InputEventCodec.KindButton, flags, State: state, PressCount: 1);

    static InputEvent MouseEvent(string id, byte state) =>
        new(id, InputEventCodec.KindButton, InputEventCodec.FlagStateChanged, State: state, PressCount: 1);

    // --- Duplicate events are deterministic and leave no residue -------------
    var keySends = new List<Win32Output>();
    var dupSink = new Win32InputSink(a => { keySends.AddRange(a); return a.Count; }, motionLoopEnabled: false);
    dupSink.HandleInput(KeyButton("key:A", InputEventCodec.StateDown));
    var downsAfterFirst = keySends.Count(k => k.Kind == Win32OutputKind.KeyDown);
    dupSink.HandleInput(KeyButton("key:A", InputEventCodec.StateDown)); // duplicate down
    Assert(keySends.Count(k => k.Kind == Win32OutputKind.KeyDown) == downsAfterFirst,
        "duplicate key DOWN must not re-send");
    Assert(dupSink.HeldKeys.Count == 1, "first DOWN keeps the key held");
    dupSink.HandleInput(KeyButton("key:A", InputEventCodec.StateUp));
    var upsAfterFirstUp = keySends.Count(k => k.Kind == Win32OutputKind.KeyUp);
    dupSink.HandleInput(KeyButton("key:A", InputEventCodec.StateUp)); // duplicate up
    Assert(keySends.Count(k => k.Kind == Win32OutputKind.KeyUp) == upsAfterFirstUp &&
           dupSink.HeldKeys.Count == 0,
        "duplicate key UP must not release again; held state empty");

    var mouseDup = new Win32InputSink(motionLoopEnabled: false);
    mouseDup.HandleInput(MouseEvent("mouse:left", InputEventCodec.StateDown));
    var mouseAfterDown = mouseDup.HeldButtons.Count;
    mouseDup.HandleInput(MouseEvent("mouse:left", InputEventCodec.StateDown));
    Assert(mouseDup.HeldButtons.Count == mouseAfterDown, "duplicate mouse DOWN must not double-track");
    mouseDup.HandleInput(MouseEvent("mouse:left", InputEventCodec.StateUp));
    mouseDup.HandleInput(MouseEvent("mouse:left", InputEventCodec.StateUp));
    Assert(mouseDup.HeldButtons.Count == 0, "double mouse UP leaves empty held state");

    // --- Keyboard / mouse state isolation ------------------------------------
    var iso = new Win32InputSink(motionLoopEnabled: false);
    iso.HandleInput(KeyButton("key:A", InputEventCodec.StateDown));
    iso.HandleInput(MouseEvent("mouse:left", InputEventCodec.StateDown));
    iso.ReleaseKeys();
    Assert(iso.HeldKeys.Count == 0 && iso.HeldButtons.Contains(Win32MouseButton.Left),
        "ReleaseKeys must not clear held mouse buttons");
    iso.HandleInput(KeyButton("key:B", InputEventCodec.StateDown));
    iso.ReleaseMouseButtons();
    Assert(iso.HeldButtons.Count == 0 && iso.HeldKeys.Contains((ushort)'B'),
        "ReleaseMouseButtons must not clear held keys");
    iso.ReleaseAll();
    Assert(iso.HeldKeys.Count == 0 && iso.HeldButtons.Count == 0,
        "combined ReleaseAll clears both isolated states");

    // --- Repeated ReleaseAll is safe ------------------------------------------
    for (var i = 0; i < 3; i++)
        iso.ReleaseAll();
    Assert(iso.HeldKeys.Count == 0 && iso.HeldButtons.Count == 0,
        "repeated ReleaseAll stays neutral without invalid releases");

    // --- Snapshot reconciliation (§15: snapshot is authoritative) -------------
    var recon = new Win32InputSink(motionLoopEnabled: false);
    recon.HandleInput(KeyButton("key:A", InputEventCodec.StateDown));
    recon.HandleInput(MouseEvent("mouse:left", InputEventCodec.StateDown));
    recon.HandleInput(MouseEvent("mouse:right", InputEventCodec.StateDown));
    Assert(recon.HeldKeys.Contains((ushort)'A') && recon.HeldButtons.Count == 2,
        "precondition: A + Left + Right held");
    recon.HandleSnapshot(new InputSnapshotPayload(new List<InputEvent>
    {
        KeyButton("key:A", InputEventCodec.StateDown,
            InputEventCodec.FlagStateChanged | InputEventCodec.FlagInitial),
        new("mouse:right", InputEventCodec.KindButton,
            InputEventCodec.FlagStateChanged | InputEventCodec.FlagInitial,
            State: InputEventCodec.StateDown, PressCount: 1),
    }));
    Assert(recon.HeldKeys.Contains((ushort)'A'), "snapshot keeps listed key held");
    Assert(new HashSet<Win32MouseButton>(recon.HeldButtons).SetEquals(new[] { Win32MouseButton.Right }),
        "snapshot releases buttons it omits and keeps the ones it lists");

    // Empty snapshot returns every snapshot-representable control to neutral.
    recon.HandleSnapshot(new InputSnapshotPayload(new List<InputEvent>
    {
        new("unused-axis", InputEventCodec.KindAxis, 0, Value: 0.5f),
    }));
    Assert(recon.HeldKeys.Count == 0 && recon.HeldButtons.Count == 0,
        "controls absent from a fresh snapshot return to neutral");
    Assert(recon.HeldKeys.Count == 0,
        "non-representable kinds do not fabricate keyboard state");

    // --- Disconnect with held input cleans everything -------------------------
    var sinkD = new TestInputSink();
    var (s1, c1, l1, f1) = CreateSession(outputSink: sinkD);
    DoHandshake(s1, c1);
    c1.Emit(MakeFrame(MessageType.InputEvent, InputEventPayloadCodec.Encode(
        new InputEventPayload(KeyButton("key:A", InputEventCodec.StateDown))), 2));
    c1.Emit(MakeFrame(MessageType.InputEvent, InputEventPayloadCodec.Encode(
        new InputEventPayload(MouseEvent("mouse:left", InputEventCodec.StateDown))), 3));
    c1.Emit(MakeFrame(MessageType.Disconnect, DisconnectPayloadCodec.Encode(new DisconnectPayload(0)), 4, mustUnderstand: true));
    Assert(sinkD.ReleaseAllCount == 1, "graceful disconnect with mixed held input releases once");
    Assert(s1.State == ServerSessionState.Closed, "session closed cleanly");

    // Transport failure mid-input also cleans up.
    var sinkF = new TestInputSink();
    var (s2, c2, l2, f2) = CreateSession(outputSink: sinkF);
    DoHandshake(s2, c2);
    c2.Emit(MakeFrame(MessageType.InputEvent, InputEventPayloadCodec.Encode(
        new InputEventPayload(KeyButton("key:A", InputEventCodec.StateDown))), 2));
    c2.EmitDisconnected("connection reset by peer");
    Assert(sinkF.ReleaseAllCount == 1, "transport failure must release held input");
    Assert(s2.State == ServerSessionState.Closed, "transport failure closes session");

    // --- New session never inherits old input state ---------------------------
    var perSessionSinks = new List<TestInputSink>();
    TestInputSink SinkFactory()
    {
        var s = new TestInputSink();
        perSessionSinks.Add(s);
        return s;
    }
    var serverA = new TcpServer(0);
    var hostA = new SessionHost(serverA, DefaultTestAuthenticator(), new RecordingSessionListener(),
        null, null, SinkFactory);
    _ = hostA.StartAsync();
    await WaitUntil(() => hostA.IsListening);

    // Session A holds an input, then dies abruptly.
    var clientA = new TcpClient();
    await clientA.ConnectAsync(IPAddress.Loopback, serverA.LocalPort);
    var connA = new TcpConnection(clientA);
    var framesA = new ConcurrentQueue<ProtocolFrame>();
    connA.FrameReceived += f => framesA.Enqueue(f);
    var runA = connA.RunAsync();
    await connA.SendAsync(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    await WaitUntil(() => framesA.Count >= 1);
    var welcomeA = WelcomePayloadCodec.Decode(framesA.First().Payload);
    await connA.SendAsync(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.TokenAuth("ctrl-42a8", welcomeA.Challenge)),
        1, mustUnderstand: true));
    await WaitUntil(() => perSessionSinks.Count >= 1 && hostA.Manager.IsActive("ctrl-42a8"));
    await connA.SendAsync(MakeFrame(MessageType.InputEvent, InputEventPayloadCodec.Encode(
        new InputEventPayload(KeyButton("key:A", InputEventCodec.StateDown))), 2));
    await Task.Delay(80);
    Assert(perSessionSinks[0].Inputs.Count == 1, "session A received its input via its own sink");
    connA.CloseAsync().Wait();
    await runA;
    await WaitUntil(() => !hostA.Manager.IsActive("ctrl-42a8"));
    Assert(perSessionSinks[0].ReleaseAllCount == 1, "session A sink released on disconnect");

    // Session B gets its own brand-new neutral sink (fresh factory instance).
    var clientB = new TcpClient();
    await clientB.ConnectAsync(IPAddress.Loopback, serverA.LocalPort);
    var connB = new TcpConnection(clientB);
    var framesB = new ConcurrentQueue<ProtocolFrame>();
    connB.FrameReceived += f => framesB.Enqueue(f);
    var runB = connB.RunAsync();
    await connB.SendAsync(MakeFrame(MessageType.Hello,
        HelloPayloadCodec.Encode(new HelloPayload("ctrl-42a8", "0.1.0", 1, 0, 0x00000007)),
        0, mustUnderstand: true));
    await WaitUntil(() => framesB.Count >= 1 && perSessionSinks.Count >= 2);
    var welcomeB = WelcomePayloadCodec.Decode(framesB.First().Payload);
    await connB.SendAsync(MakeFrame(MessageType.Auth,
        AuthPayloadCodec.Encode(AuthTestEnv.TokenAuth("ctrl-42a8", welcomeB.Challenge)),
        1, mustUnderstand: true));
    await WaitUntil(() => framesB.Count >= 2 &&
        framesB.ToArray()[1].MessageType == MessageType.AuthOk);
    // A real client sends the mandatory INPUT_SNAPSHOT right after AUTH_OK.
    await connB.SendAsync(MakeFrame(MessageType.InputSnapshot, InputSnapshotPayloadCodec.Encode(
        new InputSnapshotPayload(new List<InputEvent>
        {
            new("b", InputEventCodec.KindButton,
                InputEventCodec.FlagStateChanged | InputEventCodec.FlagInitial,
                State: InputEventCodec.StateUp, PressCount: 0),
        })), 2));
    await WaitUntil(() => perSessionSinks.Count >= 2 &&
        perSessionSinks[1].Snapshots.Count >= 1);
    Assert(perSessionSinks[1].Inputs.Count == 0,
        "session B starts with no inherited input events");
    Assert(perSessionSinks[1].ReleaseAllCount == 0,
        "session B sink starts unreleased and pristine");
    await connB.CloseAsync();
    await runB;
    await hostA.StopAsync();

    // --- Send failure diagnostics ----------------------------------------------
    var failures = 0;
    var failingSink = new Win32InputSink(_ => { failures++; return 0; }, motionLoopEnabled: false); // native send fails
    failingSink.HandleInput(KeyButton("key:A", InputEventCodec.StateDown));
    Assert(failingSink.FailedSendBatches == 1, "failed sends must be observable");
    failingSink.ReleaseAll();
    Assert(failingSink.HeldKeys.Count == 0, "release still clears logical state after send failure");
    Assert(failingSink.FailedSendBatches == 2, "failed release batch is tracked too");
    failingSink.ReleaseAll(); // repeated cleanup safe
    Assert(failingSink.FailedSendBatches == 2, "idle ReleaseAll does not touch the failed-send counter");

    Console.WriteLine("M2.3: input reliability smoke tests passed.");
}

// ---------------------------------------------------------------------------
// M2.4 — gamepad input (abstract gamepad per CAP_GAMEPAD, §8/§9/§18)
// ---------------------------------------------------------------------------

static void RunGamepadInputSmokeTests()
{
    static InputEvent GpButton(string id, byte state, byte flags = InputEventCodec.FlagStateChanged) =>
        new(id, InputEventCodec.KindButton, flags, State: state, PressCount: 1);

    static InputEvent KeyButton(string id, byte state) =>
        new(id, InputEventCodec.KindButton, InputEventCodec.FlagStateChanged, State: state, PressCount: 1);

    static InputEvent MouseEvent(string id, byte state) =>
        new(id, InputEventCodec.KindButton, InputEventCodec.FlagStateChanged, State: state, PressCount: 1);

    // --- Buttons: A/B/X/Y + shoulders + back/start + stick clicks ------------
    var sink = new Win32InputSink(motionLoopEnabled: false);
    sink.HandleInput(GpButton("gamepad:a", InputEventCodec.StateDown));
    sink.HandleInput(GpButton("gamepad:b", InputEventCodec.StateDown));
    Assert(new HashSet<Win32GamepadButton>(sink.HeldGamepadButtons)
               .SetEquals(new[] { Win32GamepadButton.A, Win32GamepadButton.B }),
        "gamepad A+B held");
    sink.HandleInput(GpButton("gamepad:a", InputEventCodec.StateUp));
    Assert(new HashSet<Win32GamepadButton>(sink.HeldGamepadButtons)
               .SetEquals(new[] { Win32GamepadButton.B }),
        "gamepad A released, B still held");

    // Duplicates.
    sink.HandleInput(GpButton("gamepad:b", InputEventCodec.StateDown)); // duplicate down
    Assert(sink.HeldGamepadButtons.Count == 1, "duplicate gamepad DOWN must not double-track");
    sink.HandleInput(GpButton("gamepad:b", InputEventCodec.StateUp));
    sink.HandleInput(GpButton("gamepad:b", InputEventCodec.StateUp)); // duplicate up
    Assert(sink.HeldGamepadButtons.Count == 0, "double gamepad UP leaves empty held state");

    // Full button vocabulary.
    foreach (var (id, expected) in new[]
             {
                 ("gamepad:x", Win32GamepadButton.X),
                 ("gamepad:y", Win32GamepadButton.Y),
                 ("gamepad:lb", Win32GamepadButton.LeftShoulder),
                 ("gamepad:rb", Win32GamepadButton.RightShoulder),
                 ("gamepad:back", Win32GamepadButton.Back),
                 ("gamepad:start", Win32GamepadButton.Start),
                 ("gamepad:lsclick", Win32GamepadButton.LeftStickClick),
                 ("gamepad:rsclick", Win32GamepadButton.RightStickClick),
             })
    {
        var mapped = Win32InputMapper.Map(GpButton(id, InputEventCodec.StateDown));
        Assert(mapped is { Kind: Win32OutputKind.GamepadButtonDown } && mapped.GamepadButton == expected,
            $"gamepad control '{id}' maps correctly");
    }

    // --- Analog sticks: value semantics preserved ----------------------------
    var stickSink = new Win32InputSink(motionLoopEnabled: false);
    stickSink.HandleInput(new InputEvent(
        "gamepad:lstick", InputEventCodec.KindStick, 0, X: -0.8f, Y: 0.4f));
    var lstick = stickSink.GamepadAxes["gamepad:lstick"];
    Assert(Math.Abs(lsticks_Fx(lstick) + 0.8f) < 0.0001f && Math.Abs(lsticks_Fy(lstick) - 0.4f) < 0.0001f,
        "left stick preserves protocol values (-0.8, 0.4)");

    stickSink.HandleInput(new InputEvent(
        "gamepad:rstick", InputEventCodec.KindStick, 0, X: 2.0f, Y: -2.0f));
    var rstick = stickSink.GamepadAxes["gamepad:rstick"];
    Assert(rstick.Fx == 1f && rstick.Fy == -1f,
        "right stick values outside -1..1 are clamped per §9");

    stickSink.HandleInput(new InputEvent(
        "gamepad:rstick", InputEventCodec.KindStick, 0, X: 0f, Y: 0f));
    Assert(stickSink.GamepadAxes["gamepad:rstick"] is { Fx: 0f, Fy: 0f },
        "neutral reset restores stick center");

    // Non-finite stick input is dropped (§18 input-plane semantics).
    var beforeNan = stickSink.GamepadAxes.Count;
    stickSink.HandleInput(new InputEvent(
        "gamepad:lstick", InputEventCodec.KindStick, 0, X: float.NaN, Y: 0f));
    Assert(stickSink.GamepadAxes.Count == beforeNan &&
           Math.Abs(stickSink.GamepadAxes["gamepad:lstick"].Fx + 0.8f) < 0.0001f,
        "NaN stick values are dropped without corrupting state");

    // --- Triggers: analog 0..1 ------------------------------------------------
    var trig = Win32InputMapper.Map(new InputEvent(
        "gamepad:rt", InputEventCodec.KindTrigger, 0, Value: 0.5f));
    Assert(trig is { Kind: Win32OutputKind.GamepadTriggerState, VirtualKey: 2 } && Math.Abs(trig.Fx - 0.5f) < 0.0001f,
        "right trigger maps intermediate analog value");
    var ltMin = Win32InputMapper.Map(new InputEvent(
        "gamepad:lt", InputEventCodec.KindTrigger, 0, Value: 0f));
    Assert(ltMin is { Kind: Win32OutputKind.GamepadTriggerState, VirtualKey: 1 } && ltMin.Fx == 0f,
        "left trigger minimum maps to 0");
    var rtMax = Win32InputMapper.Map(new InputEvent(
        "gamepad:rt", InputEventCodec.KindTrigger, 0, Value: 1f));
    Assert(rtMax.Fx == 1f, "right trigger maximum maps to 1");
    var overClamp = Win32InputMapper.Map(new InputEvent(
        "gamepad:lt", InputEventCodec.KindTrigger, 0, Value: 5f));
    Assert(overClamp.Fx == 1f, "out-of-range trigger clamps to 1 per §9 normalization");
    stickSink.HandleInput(new InputEvent(
        "gamepad:rt", InputEventCodec.KindTrigger, 0, Value: 0.75f));
    Assert(Math.Abs(stickSink.GamepadAxes["gamepad:rt"].Fx - 0.75f) < 0.0001f,
        "trigger state tracked in gamepad axes");

    // --- D-pad / hat ------------------------------------------------------------
    for (byte hat = 0; hat <= 8; hat++)
    {
        var mapped = Win32InputMapper.Map(new InputEvent(
            "gamepad:dpad", InputEventCodec.KindHat, 0, HatValue: hat));
        if (mapped.Kind != Win32OutputKind.GamepadHatState || mapped.VirtualKey != hat)
            throw new Exception($"hat {hat} must map to GamepadHatState with raw value preserved");
    }
    Assert(Win32InputMapper.Map(new InputEvent(
        "gamepad:dpad", InputEventCodec.KindHat, 0, HatValue: 9)).Kind == Win32OutputKind.None,
        "hat value 9 is invalid and dropped (§18)");

    // --- Keyboard/mouse/gamepad isolation --------------------------------------
    var iso = new Win32InputSink(motionLoopEnabled: false);
    iso.HandleInput(KeyButton("key:A", InputEventCodec.StateDown));
    iso.HandleInput(MouseEvent("mouse:left", InputEventCodec.StateDown));
    iso.HandleInput(GpButton("gamepad:a", InputEventCodec.StateDown));
    Assert(iso.HeldKeys.Contains((ushort)'A') &&
           iso.HeldButtons.Contains(Win32MouseButton.Left) &&
           iso.HeldGamepadButtons.Contains(Win32GamepadButton.A),
        "precondition: one held input in each category");
    iso.ReleaseKeys();
    Assert(iso.HeldKeys.Count == 0 && iso.HeldButtons.Contains(Win32MouseButton.Left) &&
           iso.HeldGamepadButtons.Contains(Win32GamepadButton.A),
        "ReleaseKeys leaves mouse and gamepad untouched");
    iso.ReleaseMouseButtons();
    Assert(iso.HeldButtons.Count == 0 && iso.HeldKeys.Count == 0 &&
           iso.HeldGamepadButtons.Contains(Win32GamepadButton.A),
        "ReleaseMouseButtons leaves keyboard and gamepad untouched");
    iso.HandleInput(MouseEvent("mouse:right", InputEventCodec.StateDown));
    iso.ReleaseAll();
    Assert(iso.HeldKeys.Count == 0 && iso.HeldButtons.Count == 0 && iso.HeldGamepadButtons.Count == 0
           && iso.GamepadAxes.Count == 0,
        "ReleaseAll clears all three categories plus axes");

    // --- Snapshot reconciliation includes gamepad ------------------------------
    var snap = new Win32InputSink(motionLoopEnabled: false);
    snap.HandleInput(GpButton("gamepad:a", InputEventCodec.StateDown));
    snap.HandleInput(GpButton("gamepad:b", InputEventCodec.StateDown));
    snap.HandleInput(new InputEvent(
        "gamepad:rt", InputEventCodec.KindTrigger, 0, Value: 0.5f));
    snap.HandleSnapshot(new InputSnapshotPayload(new List<InputEvent>
    {
        GpButton("gamepad:a", InputEventCodec.StateDown,
            InputEventCodec.FlagStateChanged | InputEventCodec.FlagInitial),
    }));
    Assert(new HashSet<Win32GamepadButton>(snap.HeldGamepadButtons)
               .SetEquals(new[] { Win32GamepadButton.A }),
        "snapshot keeps listed gamepad button and releases B");
    Assert(!snap.GamepadAxes.ContainsKey("gamepad:rt"),
        "snapshot omits right trigger -> axis returns to neutral");

    Console.WriteLine("M2.4: gamepad input smoke tests passed.");
}

static float lsticks_Fx((float Fx, float Fy) axis) => axis.Fx;
static float lsticks_Fy((float Fx, float Fy) axis) => axis.Fy;

sealed class TestData
{
    public static readonly byte[] SessionId = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F];

    public static readonly byte[] Challenge = Enumerable.Range(0x10, 32).Select(i => (byte)i).ToArray();
}

/// <summary>
/// M1.4.4 test environment for the real §12 authenticator: one pinned master
/// key shared by every suite (and mirrored by the Dart tests), helpers to build
/// wire-correct AUTH payloads, and a pre-paired default authenticator so the
/// M1.4.2/M1.4.3 suites keep exercising token reconnects.
/// </summary>
static class AuthTestEnv
{
    /// <summary>32-byte deterministic master key (tests only; never a secret).</summary>
    public static readonly byte[] MasterKey =
        Enumerable.Range(0, 32).Select(i => (byte)(i + 1)).ToArray();

    public const string PairingCode = "123456";

    public static byte[] Hmac(byte[] key, byte[] data)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    public static string Hex(byte[] bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    /// <summary>The persistent token bytes for [deviceId] (deterministic).</summary>
    public static byte[] TokenFor(string deviceId) => HmacAuthenticator.HmacSha256(
        MasterKey,
        Encoding.UTF8.GetBytes(HmacAuthenticator.TokenDerivationContext + deviceId));

    /// <summary>Token-flow AUTH payload: empty credential + HMAC(token, challenge).</summary>
    public static AuthPayload TokenAuth(string deviceId, byte[] challenge) =>
        new(AuthPayloadCodec.CredentialTypeToken, "", deviceId, Hmac(TokenFor(deviceId), challenge));

    /// <summary>Pairing-flow AUTH payload: code on the wire + HMAC(code, challenge).</summary>
    public static AuthPayload PairingAuth(string code, string deviceId, byte[] challenge) =>
        new(AuthPayloadCodec.CredentialTypePairingCode, code, deviceId,
            Hmac(Encoding.UTF8.GetBytes(code), challenge));

    public static PairingCodeService MakePairingService(Func<ulong>? nowMs = null, ulong ttlMs = PairingCodeService.DefaultTtlMs)
    {
        var pairing = new PairingCodeService(nowMs: nowMs, ttlMs: ttlMs);
        pairing.IssueExplicit(PairingCode);
        return pairing;
    }
}

sealed class FakeTransportConnection : ITransportConnection
{
    public bool IsConnected => Volatile.Read(ref _closed) == 0;
    public string RemoteAddress { get; set; } = "test-ip";
    public List<byte[]> Sent { get; } = new();
    public List<ProtocolFrame> SentFrames { get; } = new();
    public event Action<ProtocolFrame>? FrameReceived;
    public event Action<string>? Disconnected;
    private int _closed;

    public Task SendAsync(byte[] frame, CancellationToken cancellationToken = default)
    {
        var copy = frame.ToArray();
        Sent.Add(copy);
        SentFrames.Add(FrameCodec.Decode(copy));
        return Task.CompletedTask;
    }

    public void Emit(byte[] rawFrame) => FrameReceived?.Invoke(FrameCodec.Decode(rawFrame));

    public void EmitDisconnected(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        Disconnected?.Invoke(reason);
    }

    public Task CloseAsync()
    {
        EmitDisconnected("Connection closed.");
        return Task.CompletedTask;
    }
}

sealed class RecordingSessionListener : ISessionListener
{
    public List<ServerSessionState> States { get; } = new();
    public List<string> Errors { get; } = new();
    public List<InputEvent> InputEvents { get; } = new();
    public List<InputSnapshotPayload> Snapshots { get; } = new();

    public void OnStateChanged(ServerSessionState state) => States.Add(state);
    public void OnError(string message) => Errors.Add(message);
    public void OnInputEvent(InputEvent inputEvent) => InputEvents.Add(inputEvent);
    public void OnInputSnapshot(InputSnapshotPayload snapshot) => Snapshots.Add(snapshot);
}

sealed class RecordingFlusher : IInputStateFlusher
{
    public int FlushCount { get; private set; }
    public void Flush() => FlushCount++;
}

/// <summary>Mutable fake clock so session tests can advance time deterministically.</summary>
sealed class MutableClock
{
    public ulong Value;
    public ulong Now() => Value;
}

static class IntegrationLog
{
    public static string Name(byte type) => type switch
    {
        MessageType.Hello => "HELLO",
        MessageType.Welcome => "WELCOME",
        MessageType.Auth => "AUTH",
        MessageType.AuthOk => "AUTH_OK",
        MessageType.AuthDenied => "AUTH_DENIED",
        MessageType.InputEvent => "INPUT_EVENT",
        MessageType.InputSnapshot => "INPUT_SNAPSHOT",
        MessageType.InputReset => "INPUT_RESET",
        MessageType.Heartbeat => "HEARTBEAT",
        MessageType.Pong => "PONG",
        MessageType.Ack => "ACK",
        MessageType.Error => "ERROR",
        MessageType.Disconnect => "DISCONNECT",
        MessageType.ProfileListReq => "PROFILE_LIST_REQ",
        MessageType.ProfileList => "PROFILE_LIST",
        MessageType.ProfileSelect => "PROFILE_SELECT",
        MessageType.ProfileSelected => "PROFILE_SELECTED",
        MessageType.ConfigPush => "CONFIG_PUSH",
        MessageType.Status => "STATUS",
        MessageType.GamepadStatus => "GAMEPAD_STATUS",
        _ => $"0x{type:X2}"
    };
}

/// <summary>Integration-server listener: prints machine-readable C#: markers to
/// stdout so the Dart integration tool can assert on them cross-language.</summary>
sealed class IntegrationListener : ISessionListener
{
    public void OnStateChanged(ServerSessionState state)
    {
        Console.WriteLine($"C#:STATE:{state}");
        Console.Out.Flush();
    }

    public void OnError(string message)
    {
        Console.WriteLine($"C#:ERR:{message}");
        Console.Out.Flush();
    }

    public void OnInputEvent(InputEvent inputEvent)
    {
        Console.WriteLine($"C#:INPUT_EVENT:{inputEvent.ControlId}:{inputEvent.Kind}:" +
            $"{inputEvent.State ?? 0}:{inputEvent.PressCount ?? 0}");
        Console.Out.Flush();
    }

    public void OnInputSnapshot(InputSnapshotPayload snapshot)
    {
        Console.WriteLine($"C#:INPUT_SNAPSHOT:{snapshot.Events.Count}");
        Console.Out.Flush();
    }
}

sealed class IntegrationFlusher : IInputStateFlusher
{
    public void Flush()
    {
        Console.WriteLine("C#:FLUSH");
        Console.Out.Flush();
    }
}

/// <summary>M2.3: output sink for the integration server. Prints a RELEASED
/// marker whenever the session's held input is flushed, proving the wire →
/// decode → Session → IOutputSink cleanup path end-to-end.</summary>
sealed class IntegrationOutputSink : CTRL.Desktop.Input.IOutputSink
{
    private int _released;
    private int _inputCount;
    private int _snapshotCount;
    public int InputCount => Volatile.Read(ref _inputCount);
    public int SnapshotCount => Volatile.Read(ref _snapshotCount);
    public int ReleaseAllCount => Volatile.Read(ref _released);

    public void HandleInput(InputEvent inputEvent) => Interlocked.Increment(ref _inputCount);
    public void HandleSnapshot(InputSnapshotPayload snapshot) => Interlocked.Increment(ref _snapshotCount);
    public void ReleaseAll()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;
        Console.WriteLine("C#:RELEASED");
        Console.Out.Flush();
    }
}

/// <summary>Wraps a real transport connection and logs every frame it sends or
/// receives as C#:TX / C#:RX markers with the message type and sequence.</summary>
sealed class RecordingTransportConnection : ITransportConnection
{
    private readonly ITransportConnection _inner;
    private event Action<ProtocolFrame>? _frameReceived;
    private event Action<string>? _disconnected;

    public RecordingTransportConnection(ITransportConnection inner)
    {
        _inner = inner;
        _inner.FrameReceived += frame =>
        {
            Console.WriteLine($"C#:RX:{IntegrationLog.Name(frame.MessageType)}:{frame.Sequence}");
            Console.Out.Flush();
            _frameReceived?.Invoke(frame);
        };
        _inner.Disconnected += reason =>
        {
            Console.WriteLine($"C#:LOST:{reason}");
            Console.Out.Flush();
            _disconnected?.Invoke(reason);
        };
    }

    public bool IsConnected => _inner.IsConnected;

    /// <inheritdoc />
    public string RemoteAddress => _inner.RemoteAddress;

    public event Action<ProtocolFrame>? FrameReceived
    {
        add => _frameReceived += value;
        remove => _frameReceived -= value;
    }

    public event Action<string>? Disconnected
    {
        add => _disconnected += value;
        remove => _disconnected -= value;
    }

    public async Task SendAsync(byte[] frame, CancellationToken cancellationToken = default)
    {
        var decoded = FrameCodec.Decode(frame);
        Console.WriteLine($"C#:TX:{IntegrationLog.Name(decoded.MessageType)}:{decoded.Sequence}");
        Console.Out.Flush();
        await _inner.SendAsync(frame, cancellationToken);
    }

    public Task CloseAsync() => _inner.CloseAsync();
}
