using CTRL.Desktop.Input.Win32;

namespace CTRL.Desktop.Input;

/// <summary>
/// Output boundary for the abstract gamepad state tracked by
/// <see cref="Win32InputSink"/> (M2.5 Phase A).
///
/// Phase A ships only safe implementations: <see cref="NullGamepadBackend"/>
/// (default, no physical output) and <see cref="TestGamepadBackend"/> (recorder
/// for deterministic tests). A physical backend (e.g. WinUHid) is a separate,
 /// approved follow-up — this interface deliberately knows nothing about
/// drivers/HID/vendor APIs.
///
/// Implementations must be cheap and non-blocking; all methods are invoked on
/// the session input path. Throwing is reserved for genuine failures — the
/// sink catches and counts them so the session can never crash.
/// </summary>
public interface IGamepadOutputBackend : IDisposable
{
    /// <summary>Prepares the backend for a session. Idempotent.</summary>
    void Connect();

    /// <summary>Sets one abstract gamepad button's held state.</summary>
    void SetButton(Win32GamepadButton button, bool down);

    /// <summary>Sets the D-pad/hat raw value (0 = center, 1..8 = directions).</summary>
    void SetHat(byte hatValue);

    /// <summary>
    /// Sets an analog stick position. <paramref name="stickId"/>: 1 = left,
    /// 2 = right. Values are protocol-normalized (-1..1), already clamped.
    /// </summary>
    void SetStick(int stickId, float x, float y);

    /// <summary>
    /// Sets an analog trigger value. <paramref name="triggerId"/>: 1 = left,
    /// 2 = right. Values are protocol-normalized (0..1), already clamped.
    /// </summary>
    void SetTrigger(int triggerId, float value);

    /// <summary>Returns every gamepad output to neutral. Idempotent.</summary>
    void ReleaseAll();
}
