using System.Runtime.InteropServices;
using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Input.Win32;

/// <summary>
/// Win32 output sink. M2.0 shipped the keyboard boundary; M2.2 adds relative
/// mouse movement, L/R/M buttons and wheel scroll.
///
/// Mouse semantics (analisis-teknis.md §15): stick "mouse:move" feeds a
/// velocity that a small desktop-side loop converts to relative deltas with an
/// exponent curve; wheel axes accumulate into notches. The loop runs on a
/// ~60 Hz timer in production; tests disable it (<c>motionLoopEnabled:false</c>)
/// and drive <see cref="PumpTick"/> manually for deterministic assertions.
///
/// All Windows interop stays in this class — the session only knows
/// <see cref="IOutputSink"/>.
/// </summary>
public sealed class Win32InputSink : IOutputSink, IDisposable
{
    /// <summary>Cursor pixels per second at full stick deflection.</summary>
    public const int MaxMovePixelsPerSecond = 1200;

    /// <summary>Motion loop cadence (~60 Hz).</summary>
    public const int MotionTickMs = 16;

    /// <summary>Full-deflection wheel notches per second.</summary>
    public const double MaxWheelNotchesPerSecond = 6.0;

    private readonly object _gate = new();
    private readonly HashSet<ushort> _heldKeys = new();
    private readonly HashSet<Win32MouseButton> _heldButtons = new();
    private readonly Func<IReadOnlyList<Win32Output>, int> _send;
    private readonly bool _motionLoopEnabled;
    private readonly ITimer? _motionTimer;
    private float _moveVx;
    private float _moveVy;
    private float _wheelUpSpeed;
    private float _wheelDownSpeed;
    private double _wheelUpAccum;
    private double _wheelDownAccum;
    private bool _disposed;

    public Win32InputSink(Func<IReadOnlyList<Win32Output>, int>? send = null, bool motionLoopEnabled = true)
    {
        _send = send ?? NativeSend;
        _motionLoopEnabled = motionLoopEnabled;
        if (motionLoopEnabled)
        {
            _motionTimer = new Timer(
                _ => { try { PumpTick(); } catch { /* loop must never throw */ } },
                null, MotionTickMs, MotionTickMs);
        }
    }

    /// <summary>Virtual keys currently held down (test/diagnostic view).</summary>
    public IReadOnlyCollection<ushort> HeldKeys
    {
        get
        {
            lock (_gate)
            {
                return _heldKeys.ToArray();
            }
        }
    }

    /// <summary>Mouse buttons currently held down (test/diagnostic view).</summary>
    public IReadOnlyCollection<Win32MouseButton> HeldButtons
    {
        get
        {
            lock (_gate)
            {
                return _heldButtons.ToArray();
            }
        }
    }

    /// <summary>
    /// Number of SendInput batches whose reported sent-count fell short of the
    /// requested action count. Diagnostics only — logical state is always kept
    /// consistent (releases clear state even when the native call fails), so a
    /// failed send can never strand a stuck input silently (M2.3).
    /// </summary>
    public int FailedSendBatches { get; private set; }

    public void HandleInput(InputEvent input) => Apply(new[] { input });

    /// <summary>
    /// INPUT_SNAPSHOT is authoritative (docs/protocol.md §15: the payload
    /// carries the FULL current control state). Reconciliation therefore:
    ///   - presses every control held down by the snapshot,
    ///   - releases any currently-held control the snapshot does not list,
    ///   - resets motion/wheel axes the snapshot does not restate.
    /// </summary>
    public void HandleSnapshot(InputSnapshotPayload snapshot)
    {
        var events = snapshot.Events;
        var instantActions = new List<Win32Output>(events.Count);
        lock (_gate)
        {
            var snapshotKeys = new HashSet<ushort>();
            var snapshotButtons = new HashSet<Win32MouseButton>();

            foreach (var input in events)
            {
                var mapped = Win32InputMapper.Map(input);
                switch (mapped.Kind)
                {
                    case Win32OutputKind.KeyDown:
                        if (snapshotKeys.Add(mapped.VirtualKey) && _heldKeys.Add(mapped.VirtualKey))
                            instantActions.Add(mapped);
                        break;
                    case Win32OutputKind.MouseDown:
                        if (snapshotButtons.Add(mapped.MouseButton) && _heldButtons.Add(mapped.MouseButton))
                            instantActions.Add(mapped);
                        break;
                    case Win32OutputKind.MoveVelocity:
                        _moveVx = mapped.Fx;
                        _moveVy = mapped.Fy;
                        break;
                    case Win32OutputKind.WheelVelocity:
                        if (mapped.VirtualKey == Win32InputMapper.WheelDirectionUp)
                            _wheelUpSpeed = mapped.Fx;
                        else
                            _wheelDownSpeed = mapped.Fx;
                        break;
                }
            }

            // Authoritative reconciliation: drop whatever the snapshot omits.
            foreach (var vk in _heldKeys.Where(k => !snapshotKeys.Contains(k)).ToArray())
            {
                _heldKeys.Remove(vk);
                instantActions.Add(Win32Output.Key(Win32OutputKind.KeyUp, vk, Win32InputMapper.IsExtended(vk)));
            }
            foreach (var button in _heldButtons.Where(b => !snapshotButtons.Contains(b)).ToArray())
            {
                _heldButtons.Remove(button);
                instantActions.Add(Win32Output.Mouse(Win32OutputKind.MouseUp, button));
            }
        }
        if (instantActions.Count > 0)
            SendTracked(instantActions);
    }

    /// <summary>
    /// One motion-loop iteration: converts stored stick velocity into relative
    /// cursor deltas and wheel axis speeds into ±120 notches. Public so tests
    /// can drive it deterministically when the real timer is disabled.
    /// </summary>
    public void PumpTick()
    {
        List<Win32Output> outputs = new();
        lock (_gate)
        {
            // Relative cursor movement (exponent curve: fine control near center).
            var magnitude = Math.Sqrt(_moveVx * _moveVx + _moveVy * _moveVy);
            if (magnitude > 0.001)
            {
                var scaled = Math.Pow(magnitude, 1.6) / magnitude; // curve keeps direction
                var seconds = MotionTickMs / 1000.0;
                var dx = (int)Math.Round(_moveVx * scaled * MaxMovePixelsPerSecond * seconds);
                var dy = (int)Math.Round(_moveVy * scaled * MaxMovePixelsPerSecond * seconds);
                if (dx != 0 || dy != 0)
                    outputs.Add(new Win32Output(Win32OutputKind.MoveVelocity, 0, false,
                        Win32MouseButton.None, dx, dy));
            }

            // Wheel notch accumulation.
            _wheelUpAccum += _wheelUpSpeed * MaxWheelNotchesPerSecond * MotionTickMs / 1000.0;
            while (_wheelUpAccum >= 1.0)
            {
                _wheelUpAccum -= 1.0;
                outputs.Add(Wheel(Win32InputMapper.WheelDirectionUp));
            }
            _wheelDownAccum += _wheelDownSpeed * MaxWheelNotchesPerSecond * MotionTickMs / 1000.0;
            while (_wheelDownAccum >= 1.0)
            {
                _wheelDownAccum -= 1.0;
                outputs.Add(Wheel(Win32InputMapper.WheelDirectionDown));
            }
        }
        if (outputs.Count > 0)
            SendTracked(outputs);
    }

    /// <summary>Returns every output to neutral: keys, mouse buttons and
    /// motion state (docs/protocol.md §16). Idempotent.</summary>
    public void ReleaseAll()
    {
        ReleaseKeys();
        ReleaseMouseButtons();
        lock (_gate)
        {
            // Motion state resets to neutral too (§16).
            _moveVx = _moveVy = 0;
            _wheelUpSpeed = _wheelDownSpeed = 0;
            _wheelUpAccum = _wheelDownAccum = 0;
        }
    }

    /// <summary>Releases only held keyboard keys. Isolated from mouse state
    /// (M2.3 reliability matrix); idempotent.</summary>
    public void ReleaseKeys()
    {
        List<Win32Output> releases;
        lock (_gate)
        {
            if (_heldKeys.Count == 0)
                return;
            releases = _heldKeys
                .Select(vk => Win32Output.Key(Win32OutputKind.KeyUp, vk, Win32InputMapper.IsExtended(vk)))
                .ToList();
            _heldKeys.Clear();
        }
        SendTracked(releases);
    }

    /// <summary>Releases only held mouse buttons. Isolated from keyboard state;
    /// idempotent.</summary>
    public void ReleaseMouseButtons()
    {
        List<Win32Output> releases;
        lock (_gate)
        {
            if (_heldButtons.Count == 0)
                return;
            releases = _heldButtons
                .Select(b => Win32Output.Mouse(Win32OutputKind.MouseUp, b))
                .ToList();
            _heldButtons.Clear();
        }
        SendTracked(releases);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _motionTimer?.Dispose();
    }

    private static Win32Output Wheel(ushort direction) =>
        new(Win32OutputKind.WheelVelocity, direction, true,
            Win32MouseButton.None,
            direction == Win32InputMapper.WheelDirectionUp ? NativeMethods.WHEEL_DELTA : -NativeMethods.WHEEL_DELTA,
            0);

    private void Apply(IReadOnlyList<InputEvent> events)
    {
        var instantActions = new List<Win32Output>(events.Count);
        lock (_gate)
        {
            foreach (var input in events)
            {
                var mapped = Win32InputMapper.Map(input);
                switch (mapped.Kind)
                {
                    case Win32OutputKind.KeyDown:
                        if (_heldKeys.Add(mapped.VirtualKey))
                            instantActions.Add(mapped);
                        break;
                    case Win32OutputKind.KeyUp:
                        if (_heldKeys.Remove(mapped.VirtualKey))
                            instantActions.Add(mapped);
                        break;
                    case Win32OutputKind.MouseDown:
                        if (_heldButtons.Add(mapped.MouseButton))
                            instantActions.Add(mapped);
                        break;
                    case Win32OutputKind.MouseUp:
                        if (_heldButtons.Remove(mapped.MouseButton))
                            instantActions.Add(mapped);
                        break;
                    case Win32OutputKind.MoveVelocity:
                        _moveVx = mapped.Fx;
                        _moveVy = mapped.Fy;
                        break;
                    case Win32OutputKind.WheelVelocity:
                        if (mapped.VirtualKey == Win32InputMapper.WheelDirectionUp)
                            _wheelUpSpeed = mapped.Fx;
                        else
                            _wheelDownSpeed = mapped.Fx;
                        break;
                }
            }
        }
        if (instantActions.Count > 0)
            SendTracked(instantActions);
    }

    /// <summary>Sends via the injected/native path and records shortfalls so a
    /// failed batch is observable instead of silently pretending success.</summary>
    private void SendTracked(IReadOnlyList<Win32Output> actions)
    {
        var sent = _send(actions);
        if (sent < actions.Count)
            FailedSendBatches++;
    }

    private static int NativeSend(IReadOnlyList<Win32Output> outputs)
    {
        var inputs = new NativeMethods.INPUT[outputs.Count];
        for (var i = 0; i < outputs.Count; i++)
        {
            var output = outputs[i];
            switch (output.Kind)
            {
                case Win32OutputKind.KeyDown:
                case Win32OutputKind.KeyUp:
                {
                    uint flags = 0;
                    if (output.Kind == Win32OutputKind.KeyUp)
                        flags |= NativeMethods.KEYEVENTF_KEYUP;
                    if (output.Extended)
                        flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
                    inputs[i] = Keyboard(output.VirtualKey, flags);
                    break;
                }
                case Win32OutputKind.MouseDown:
                case Win32OutputKind.MouseUp:
                {
                    uint flags = output.MouseButton switch
                    {
                        Win32MouseButton.Left => output.Kind == Win32OutputKind.MouseDown
                            ? NativeMethods.MOUSEEVENTF_LEFTDOWN
                            : NativeMethods.MOUSEEVENTF_LEFTUP,
                        Win32MouseButton.Right => output.Kind == Win32OutputKind.MouseDown
                            ? NativeMethods.MOUSEEVENTF_RIGHTDOWN
                            : NativeMethods.MOUSEEVENTF_RIGHTUP,
                        Win32MouseButton.Middle => output.Kind == Win32OutputKind.MouseDown
                            ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN
                            : NativeMethods.MOUSEEVENTF_MIDDLEUP,
                        _ => 0,
                    };
                    inputs[i] = Mouse(flags, 0, 0, 0);
                    break;
                }
                case Win32OutputKind.MoveVelocity:
                    inputs[i] = Mouse(NativeMethods.MOUSEEVENTF_MOVE,
                        (int)output.Fx, (int)output.Fy, 0);
                    break;
                case Win32OutputKind.WheelVelocity:
                    inputs[i] = Mouse(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0,
                        (uint)(int)output.Fx);
                    break;
                default:
                    inputs[i] = Keyboard(0, 0);
                    break;
            }
        }
        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        return inputs.Length;
    }

    private static NativeMethods.INPUT Keyboard(ushort vk, uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = flags },
        },
    };

    private static NativeMethods.INPUT Mouse(uint flags, int dx, int dy, uint mouseData) => new()
    {
        type = NativeMethods.INPUT_MOUSE,
        U = new NativeMethods.InputUnion
        {
            mi = new NativeMethods.MOUSEINPUT
            { dx = dx, dy = dy, mouseData = mouseData, dwFlags = flags },
        },
    };

    /// <summary>SendInput P/Invoke (docs/protocol.md §22: no driver dependency).</summary>
    private static class NativeMethods
    {
        public const uint INPUT_KEYBOARD = 1;
        public const uint INPUT_MOUSE = 0;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const int WHEEL_DELTA = 120;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }
    }
}
