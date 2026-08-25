using CTRL.Desktop.Input.Win32;

namespace CTRL.Desktop.Input;

/// <summary>
/// Default <see cref="IGamepadOutputBackend"/>: performs no physical output.
/// Always safe, never throws for normal operations, and preserves M2.4
/// behavior exactly when no physical backend is configured. All gamepad state
/// continues to live in <see cref="Win32InputSink"/>.
/// </summary>
public sealed class NullGamepadBackend : IGamepadOutputBackend
{
    public static readonly NullGamepadBackend Instance = new();

    public void Connect()
    {
    }

    public void SetButton(Win32GamepadButton button, bool down)
    {
    }

    public void SetHat(byte hatValue)
    {
    }

    public void SetStick(int stickId, float x, float y)
    {
    }

    public void SetTrigger(int triggerId, float value)
    {
    }

    public void ReleaseAll()
    {
    }

    public void Dispose()
    {
    }
}
