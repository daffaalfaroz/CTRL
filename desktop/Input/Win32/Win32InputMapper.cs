using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Input.Win32;

public enum Win32OutputKind
{
    None,
    KeyDown,
    KeyUp,
}

/// <summary>A single mapped output action for the Win32 platform layer.</summary>
public readonly record struct Win32Output(Win32OutputKind Kind, ushort VirtualKey)
{
    /// <summary>Event carries nothing this sink can act on in M2.0.</summary>
    public static Win32Output None => default;
}

/// <summary>
/// Pure controlId → Win32 mapping (docs/protocol.md §18: the desktop owns the
/// binding). M2.0 ships the minimal keyboard convention so the boundary is
/// testable end-to-end; pointer/axis/gamepad mappings arrive in later
/// milestones and simply map to <see cref="Win32OutputKind.None"/> for now.
///
/// Recognized controlIds (desktop-side binding, not a wire change):
///   key:&lt;VK&gt;   e.g. "key:65" or "key:0x41"  → virtual-key code
///
/// Button semantics:
///   state Down + (StateChanged or Initial) → KeyDown
///   state Up   + (StateChanged or Initial) → KeyUp
/// </summary>
public static class Win32InputMapper
{
    public static Win32Output Map(InputEvent input)
    {
        if (input.Kind != InputEventCodec.KindButton)
            return Win32Output.None;

        var vk = TryParseVirtualKey(input.ControlId);
        if (vk is null)
            return Win32Output.None;

        var applies = (input.Flags & (InputEventCodec.FlagStateChanged | InputEventCodec.FlagInitial)) != 0;
        if (!applies)
            return Win32Output.None;

        return input.State == InputEventCodec.StateDown
            ? new Win32Output(Win32OutputKind.KeyDown, vk.Value)
            : new Win32Output(Win32OutputKind.KeyUp, vk.Value);
    }

    /// <summary>Parses "key:65" / "key:0x41" style controlIds; null otherwise.</summary>
    public static ushort? TryParseVirtualKey(string controlId)
    {
        const string prefix = "key:";
        if (!controlId.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var value = controlId[prefix.Length..].Trim();
        if (value.Length == 0 || value.Length > 6)
            return null;

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
}
