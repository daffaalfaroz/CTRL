using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Input.Win32;

public enum Win32OutputKind
{
    None,
    KeyDown,
    KeyUp,
}

/// <summary>A single mapped output action for the Win32 platform layer.</summary>
public readonly record struct Win32Output(Win32OutputKind Kind, ushort VirtualKey, bool Extended)
{
    /// <summary>Event carries nothing this sink can act on.</summary>
    public static Win32Output None => default;
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
        if (input.Kind != InputEventCodec.KindButton)
            return Win32Output.None;

        var vk = TryResolveVirtualKey(input.ControlId);
        if (vk is null)
            return Win32Output.None;

        var applies = (input.Flags & (InputEventCodec.FlagStateChanged | InputEventCodec.FlagInitial)) != 0;
        if (!applies)
            return Win32Output.None;

        return input.State == InputEventCodec.StateDown
            ? new Win32Output(Win32OutputKind.KeyDown, vk.Value, IsExtended(vk.Value))
            : new Win32Output(Win32OutputKind.KeyUp, vk.Value, IsExtended(vk.Value));
    }

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
