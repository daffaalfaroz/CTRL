namespace CTRL.Desktop.Protocol;

/// <summary>
/// CTRL message type constants (docs/protocol.md §6). Central dispatcher for
/// the session layer so handlers can be registered per type.
/// </summary>
public static class MessageType
{
    public const byte Hello = 0x01;
    public const byte Welcome = 0x02;
    public const byte Auth = 0x03;
    public const byte AuthOk = 0x04;
    public const byte AuthDenied = 0x05;
    public const byte InputEvent = 0x06;
    public const byte InputSnapshot = 0x07;
    public const byte InputReset = 0x08;
    public const byte Heartbeat = 0x09;
    public const byte Pong = 0x0A;
    public const byte Ack = 0x0B;
    public const byte Error = 0x0C;
    public const byte Disconnect = 0x0D;
    public const byte ProfileListReq = 0x0E;
    public const byte ProfileList = 0x0F;
    public const byte ProfileSelect = 0x10;
    public const byte ProfileSelected = 0x11;
    public const byte ConfigPush = 0x12;
    public const byte Status = 0x13;
    public const byte GamepadStatus = 0x14;

    /// <summary>Input-plane messages carry input records; malformed input is
    /// dropped (log) instead of closing the connection (§18, §21.7).</summary>
    public static bool IsInputPlane(byte type) =>
        type is InputEvent or InputSnapshot;

    /// <summary>Wajib-dipahami per docs/protocol.md §6: unknown receivers must
    /// answer ERROR unsupported-message + close when these are sent.</summary>
    public static bool IsMustUnderstand(byte type) =>
        type is Hello or Welcome or Auth or AuthOk or AuthDenied or Error or
            Disconnect or ProfileListReq or ProfileList or ProfileSelect or
            ProfileSelected or ConfigPush;
}