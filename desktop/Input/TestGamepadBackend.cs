using System.Text;
using CTRL.Desktop.Input.Win32;

namespace CTRL.Desktop.Input;

/// <summary>
/// Recording <see cref="IGamepadOutputBackend"/> for deterministic smoke
/// tests: every call is appended to <see cref="Log"/> in order, and a failure
/// can be injected per operation name to exercise the sink's error handling.
/// </summary>
public sealed class TestGamepadBackend : IGamepadOutputBackend
{
    private readonly object _gate = new();
    private int _nextId = 1;

    /// <summary>Ordered record of every operation, e.g. "SetButton:A:down".</summary>
    public List<string> Log { get; } = new();

    public HashSet<Win32GamepadButton> HeldButtons { get; } = new();
    public byte? LastHat { get; private set; }
    public Dictionary<int, (float X, float Y)> Sticks { get; } = new();
    public Dictionary<int, float> Triggers { get; } = new();
    public bool Connected { get; private set; }
    public int ReleaseAllCount { get; private set; }
    public int Disposals { get; private set; }

    /// <summary>When non-null, the named operation throws this exception.</summary>
    public Func<string, Exception>? FailureInjection { get; set; }

    private void Record(string operation)
    {
        lock (_gate)
        {
            Log.Add(operation);
        }
        if (FailureInjection?.Invoke(operation) is { } failure)
            throw failure;
    }

    public void Connect()
    {
        Connected = true;
        Record($"Connect:{_nextId++}");
    }

    public void SetButton(Win32GamepadButton button, bool down)
    {
        if (down)
            HeldButtons.Add(button);
        else
            HeldButtons.Remove(button);
        Record($"SetButton:{button}:{(down ? "down" : "up")}");
    }

    public void SetHat(byte hatValue)
    {
        LastHat = hatValue;
        Record($"SetHat:{hatValue}");
    }

    public void SetStick(int stickId, float x, float y)
    {
        Sticks[stickId] = (x, y);
        Record($"SetStick:{stickId}:{x}:{y}");
    }

    public void SetTrigger(int triggerId, float value)
    {
        Triggers[triggerId] = value;
        Record($"SetTrigger:{triggerId}:{value}");
    }

    public void ReleaseAll()
    {
        ReleaseAllCount++;
        HeldButtons.Clear();
        Sticks.Clear();
        Triggers.Clear();
        LastHat = null;
        Record("ReleaseAll");
    }

    public void Dispose()
    {
        Disposals++;
        Record("Dispose");
    }

    public string Dump() => string.Join(" | ", Log);
}
