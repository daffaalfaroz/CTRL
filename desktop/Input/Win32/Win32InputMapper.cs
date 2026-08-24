using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Input.Win32;

public enum Win32OutputKind
{
    None,
    KeyDown,
    KeyUp,
    MouseDown,
    MouseUp,

    /// <summary>Stateful: stick position feeds the motion loop (M2.2).</summary>
    MoveVelocity,

    /// <summary>Stateful: axis value feeds the wheel notch accumulator.</summary>
    WheelVelocity,
}

/// <summary>Mouse buttons recognized by the CTRL binding (M2.2).</summary>
public enum Win32MouseButton
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 3,
}

/// <summary>
/// A single mapped output action for the Win32 platform layer.
/// Flat struct with kind-dependent fields (documented per <see cref="Kind"/>):
///   KeyDown/KeyUp        → VirtualKey (+ Extended)
///   MouseDown/MouseUp    → MouseButton
///   MoveVelocity         → Fx/Fy = normalized velocity components (-1..1)
///   WheelVelocity        → Fx = speed (0..1), VirtualKey = direction
///                          (0x01 up / 0xFF down — see TryMapWheelDirection)
/// </summary>
public readonly record struct Win32Output(
    Win32OutputKind Kind,
    ushort VirtualKey,
    bool Extended,
    Win32MouseButton MouseButton,
    float Fx,
    float Fy)
{
    /// <summary>Event carries nothing this sink can act on.</summary>
    public static Win32Output None => default;

    public static Win32Output Key(Win32OutputKind kind, ushort vk, bool extended) =>
        new(kind, vk, extended, Win32MouseButton.None, 0, 0);

    public static Win32Output Mouse(Win32OutputKind kind, Win32MouseButton button) =>
        new(kind, 0, false, button, 0, 0);
}

/// <summary>
/// Pure controlId → Win32 keyboard mapping (docs/protocol.md §18: the desktop
/// owns the binding; M2.1 scope is keyboard only). The same convention lives on
/// the mobile side in `mobile/lib/input/keyboard_bindings.dart` — keep both
/// tables in sync.
///
/// Recognized controlIds:
///   key:&lt;VK&gt;    decimal ("key:65") or hex ("key:0x41") virtual-key code
///   key:&lt;NAME&gt;  named key from <see cref="NamedKeys"/> (case-insensitive),
///              e.g. "key:A", "key:SPACE", "key:LCONTROL", "key:F5"
///
/// Button semantics (§8):
///   state Down + (stateChanged or initial) → KeyDown
///   state Up   + (stateChanged or initial) → KeyUp
///
/// Keys that require KEYEVENTF_EXTENDEDKEY with SendInput (arrows, Ins/Del,
/// Home/End/PgUp/PgDn, RCTRL/RALT and friends) are flagged via
/// <see cref="Win32Output.Extended"/>.
/// </summary>
public static class Win32InputMapper
{
    /// <summary>Named keys accepted by the "key:NAME" convention. Values are
    /// Windows virtual-key codes; names mirror the Win32 VK_ constants without
    /// the prefix so both sides of CTRL can share one table.</summary>
    public static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // Letters & digits.
        ["SPACE"] = 0x20,
        // Navigation / editing.
        ["BACKSPACE"] = 0x08,
        ["TAB"] = 0x09,
        ["ENTER"] = 0x0D,
        ["SHIFT"] = 0x10,
        ["CONTROL"] = 0x11,
        ["ALT"] = 0x12,
        ["ESCAPE"] = 0x1B,
        ["PRIOR"] = 0x21,
        ["NEXT"] = 0x22,
        ["END"] = 0x23,
        ["HOME"] = 0x24,
        ["LEFT"] = 0x25,
        ["UP"] = 0x26,
        ["RIGHT"] = 0x27,
        ["DOWN"] = 0x28,
        ["INSERT"] = 0x2D,
        ["DELETE"] = 0x2E,
        // Modifiers (explicit sides).
        ["LSHIFT"] = 0xA0,
        ["RSHIFT"] = 0xA1,
        ["LCONTROL"] = 0xA2,
        ["RCONTROL"] = 0xA3,
        ["LMENU"] = 0xA4,
        ["RMENU"] = 0xA5,
        ["LWIN"] = 0x5B,
        ["RWIN"] = 0x5C,
        // Locks & misc.
        ["CAPITAL"] = 0x14,
        ["NUMLOCK"] = 0x90,
        ["SCROLL"] = 0x91,
        ["PRINTSCREEN"] = 0x2C,
        ["APPS"] = 0x5D,
        ["NUMPAD0"] = 0x60,
        ["NUMPAD1"] = 0x61,
        ["NUMPAD2"] = 0x62,
        ["NUMPAD3"] = 0x63,
        ["NUMPAD4"] = 0x64,
        ["NUMPAD5"] = 0x65,
        ["NUMPAD6"] = 0x66,
        ["NUMPAD7"] = 0x67,
        ["NUMPAD8"] = 0x68,
        ["NUMPAD9"] = 0x69,
        ["MULTIPLY"] = 0x6A,
        ["ADD"] = 0x6B,
        ["SUBTRACT"] = 0x6D,
        ["DECIMAL"] = 0x6E,
        ["DIVIDE"] = 0x6F,
    };

    static Win32InputMapper()
    {
        // Letters A..Z map to their ASCII codes ('A' = 0x41).
        for (var c = 'A'; c <= 'Z'; c++)
            NamedKeys[c.ToString()] = (ushort)c;
        // Digits 0..9 map to 0x30..0x39.
        for (var d = '0'; d <= '9'; d++)
            NamedKeys[d.ToString()] = (ushort)d;
        // Function keys F1..F24 → 0x70..0x87.
        for (var f = 1; f <= 24; f++)
            NamedKeys[$"F{f}"] = (ushort)(0x70 + f - 1);
    }

    private static readonly HashSet<ushort> ExtendedKeys = new()
    {
        0x21, 0x22,             // PRIOR / NEXT
        0x23, 0x24,             // END / HOME
        0x25, 0x26, 0x27, 0x28, // LEFT / UP / RIGHT / DOWN
        0x2C,                   // PRINTSCREEN
        0x2D, 0x2E,             // INSERT / DELETE
        0x5B, 0x5C, 0x5D,       // LWIN / RWIN / APPS
        0xA3, 0xA5,             // RCONTROL / RMENU
        0x6F,                   // NUMPAD DIVIDE
    };

    public static Win32Output Map(InputEvent input)
    {
        switch (input.Kind)
        {
            case InputEventCodec.KindButton:
                return MapButton(input);
            case InputEventCodec.KindStick:
                return MapStick(input);
            case InputEventCodec.KindAxis:
                return MapWheelAxis(input);
            default:
                // Trigger/hat mappings are outside M2.2 scope.
                return Win32Output.None;
        }
    }

    private static Win32Output MapButton(InputEvent input)
    {
        var button = TryMapMouseButton(input.ControlId);
        if (button is null)
            return MapKey(input);
        if ((input.Flags & (InputEventCodec.FlagStateChanged | InputEventCodec.FlagInitial)) == 0)
            return Win32Output.None;
        return input.State == InputEventCodec.StateDown
            ? Win32Output.Mouse(Win32OutputKind.MouseDown, button.Value)
            : Win32Output.Mouse(Win32OutputKind.MouseUp, button.Value);
    }

    private static Win32Output MapKey(InputEvent input)
    {
        var vk = TryResolveVirtualKey(input.ControlId);
        if (vk is null)
            return Win32Output.None;

        if ((input.Flags & (InputEventCodec.FlagStateChanged | InputEventCodec.FlagInitial)) == 0)
            return Win32Output.None;

        return input.State == InputEventCodec.StateDown
            ? Win32Output.Key(Win32OutputKind.KeyDown, vk.Value, IsExtended(vk.Value))
            : Win32Output.Key(Win32OutputKind.KeyUp, vk.Value, IsExtended(vk.Value));
    }

    /// <summary>Stick "mouse:move" drives relative cursor velocity (§15 of
    /// analisis-teknis.md: stick → speed delta on a desktop-side loop).</summary>
    private static Win32Output MapStick(InputEvent input)
    {
        if (!string.Equals(input.ControlId, MoveControlId, StringComparison.OrdinalIgnoreCase))
            return Win32Output.None;
        return new Win32Output(Win32OutputKind.MoveVelocity, 0, false,
            Win32MouseButton.None, Clamp11(input.X ?? 0), Clamp11(input.Y ?? 0));
    }

    /// <summary>Axis "mouse:wheelup"/"mouse:wheeldown" (0..1) drives wheel notch rate.</summary>
    private static Win32Output MapWheelAxis(InputEvent input)
    {
        var dir = TryMapWheelDirection(input.ControlId);
        if (dir is null)
            return Win32Output.None;
        var speed = Math.Clamp((double)(input.Value ?? 0), 0.0, 1.0);
        return new Win32Output(Win32OutputKind.WheelVelocity, (ushort)dir.Value, false,
            Win32MouseButton.None, (float)speed, 0);
    }

    public const string MoveControlId = "mouse:move";
    public const string WheelUpControlId = "mouse:wheelup";
    public const string WheelDownControlId = "mouse:wheeldown";
    public const ushort WheelDirectionUp = 0x01;
    public const ushort WheelDirectionDown = 0xFF;

    private static Win32MouseButton? TryMapMouseButton(string controlId) =>
        controlId switch
        {
            "mouse:left" => Win32MouseButton.Left,
            "mouse:right" => Win32MouseButton.Right,
            "mouse:middle" => Win32MouseButton.Middle,
            _ => null,
        };

    private static ushort? TryMapWheelDirection(string controlId) =>
        controlId switch
        {
            WheelUpControlId => WheelDirectionUp,
            WheelDownControlId => WheelDirectionDown,
            _ => null,
        };

    private static float Clamp11(float v) => Math.Clamp(v, -1f, 1f);

    /// <summary>
    /// Resolves "key:&lt;VK&gt;" (decimal/hex) and "key:&lt;NAME&gt;" controlIds.
    /// Returns null for anything that is not a keyboard key.
    /// </summary>
    public static ushort? TryResolveVirtualKey(string controlId)
    {
        const string prefix = "key:";
        if (!controlId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var value = controlId[prefix.Length..].Trim();
        if (value.Length == 0 || value.Length > 16)
            return null;

        if (NamedKeys.TryGetValue(value, out var named))
            return named;

        try
        {
            var parsed = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt32(value[2..], 16)
                : Convert.ToUInt32(value, 10);
            if (parsed is < 0x01 or > 0xFE)
                return null;
            return (ushort)parsed;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>True when SendInput needs KEYEVENTF_EXTENDEDKEY for this VK.</summary>
    public static bool IsExtended(ushort virtualKey) => ExtendedKeys.Contains(virtualKey);

    /// <summary>Builds the canonical controlId for a named key (mobile helper parity).</summary>
    public static string ControlIdFor(string keyName)
    {
        if (!NamedKeys.ContainsKey(keyName))
            throw new KeyNotFoundException($"unknown key name '{keyName}'");
        return $"key:{keyName.ToUpperInvariant()}";
    }
}
