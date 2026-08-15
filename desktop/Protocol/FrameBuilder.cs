namespace CTRL.Desktop.Protocol;

/// <summary>
/// Convenience builder for encoded frames. The session layer uses this to keep
/// control of sequence numbers and flags (D1/D2) instead of letting the codec
/// decide. Encode still rejects reserved flags defensively.
/// </summary>
public static class FrameBuilder
{
    public static byte[] Build(
        byte messageType,
        byte[] payload,
        ushort sequence,
        bool ackRequested = false,
        bool mustUnderstand = false,
        byte versionMajor = 1,
        byte versionMinor = 0,
        ulong timestamp = 0)
    {
        var flags = (byte)0;
        if (ackRequested)
            flags |= FrameCodec.AckRequested;
        if (mustUnderstand)
            flags |= FrameCodec.MustUnderstand;

        return FrameCodec.Encode(new ProtocolFrame(
            versionMajor,
            versionMinor,
            flags,
            messageType,
            sequence,
            timestamp,
            payload));
    }
}