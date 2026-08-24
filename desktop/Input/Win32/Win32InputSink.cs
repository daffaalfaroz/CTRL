using System.Runtime.InteropServices;
using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Input.Win32;

/// <summary>
/// Minimal Win32 output sink: forwards mapped keyboard events to the OS via
/// SendInput and releases everything on demand (§16 flush). M2.0 scope is the
/// keyboard boundary only — pointer/axis/gamepad outputs are ignored by the
/// mapper until later milestones.
///
/// The send path is injectable so tests can verify down/up sequencing and
/// ReleaseAll bookkeeping without injecting real input into the host session.
/// </summary>
public sealed class Win32InputSink : IOutputSink
{
    private readonly object _gate = new();
    private readonly HashSet<ushort> _heldKeys = new();
    private readonly Func<IReadOnlyList<Win32Output>, int> _send;

    public Win32InputSink(Func<IReadOnlyList<Win32Output>, int>? send = null)
    {
        _send = send ?? NativeSend;
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

    public void HandleInput(InputEvent input) => Apply(new[] { input });

    public void HandleSnapshot(InputSnapshotPayload snapshot) => Apply(snapshot.Events);

    public void ReleaseAll()
    {
        List<Win32Output> releases;
        lock (_gate)
        {
            if (_heldKeys.Count == 0)
                return;
            releases = _heldKeys
                .Select(vk => new Win32Output(Win32OutputKind.KeyUp, vk, Win32InputMapper.IsExtended(vk)))
                .ToList();
            _heldKeys.Clear();
        }
        _send(releases);
    }

    private void Apply(IReadOnlyList<InputEvent> events)
    {
        var actions = new List<Win32Output>(events.Count);
        lock (_gate)
        {
            foreach (var input in events)
            {
                var mapped = Win32InputMapper.Map(input);
                switch (mapped.Kind)
                {
                    case Win32OutputKind.KeyDown:
                        if (_heldKeys.Add(mapped.VirtualKey))
                            actions.Add(mapped);
                        break;
                    case Win32OutputKind.KeyUp:
                        if (_heldKeys.Remove(mapped.VirtualKey))
                            actions.Add(mapped);
                        break;
                }
            }
        }
        if (actions.Count > 0)
            _send(actions);
    }

    private static int NativeSend(IReadOnlyList<Win32Output> outputs)
    {
        var inputs = new NativeMethods.INPUT[outputs.Count];
        for (var i = 0; i < outputs.Count; i++)
        {
            uint flags = 0;
            if (outputs[i].Kind == Win32OutputKind.KeyUp)
                flags |= NativeMethods.KEYEVENTF_KEYUP;
            if (outputs[i].Extended)
                flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;

            inputs[i] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = (ushort)outputs[i].VirtualKey,
                        dwFlags = flags,
                    },
                },
            };
        }
        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        return inputs.Length;
    }

    /// <summary>SendInput P/Invoke (docs/protocol.md §22: no driver dependency).</summary>
    private static class NativeMethods
    {
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

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
