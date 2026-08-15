using CTRL.Desktop.Protocol;

RunFrameCodecSmokeTests();
RunPayloadPrimitiveSmokeTests();
RunHelloCodecSmokeTests();
RunWelcomeCodecSmokeTests();

Console.WriteLine("M1.1 + M1.2.1 protocol smoke tests passed.");

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
