using CTRL.Desktop.Protocol;

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
if (decoded != frame || !decoded.Payload.SequenceEqual(frame.Payload))
    throw new Exception("Frame round-trip failed.");

ExpectProtocolException(() => FrameCodec.Decode(encoded[..^1]), "truncated frame");
ExpectProtocolException(() => FrameCodec.Decode([0x00, 0x00, ..encoded[2..]]), "invalid magic");
ExpectProtocolException(() => FrameCodec.Decode([..encoded[..4], 0x02, ..encoded[5..]]), "reserved flag");

Console.WriteLine("M1.1 protocol frame codec smoke tests passed.");

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
