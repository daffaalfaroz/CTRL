using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Input;

/// <summary>Fake IOutputSink that records every call for smoke tests.</summary>
public sealed class TestInputSink : IOutputSink
{
    public List<InputEvent> Inputs { get; } = new();
    public List<InputSnapshotPayload> Snapshots { get; } = new();
    public int ReleaseAllCount { get; private set; }

    /// <summary>When set, HandleInput/HandleSnapshot throw (safety tests).</summary>
    public bool ThrowOnHandle { get; set; }

    public void HandleInput(InputEvent input)
    {
        if (ThrowOnHandle)
            throw new InvalidOperationException("sink failure");
        Inputs.Add(input);
    }

    public void HandleSnapshot(InputSnapshotPayload snapshot)
    {
        if (ThrowOnHandle)
            throw new InvalidOperationException("sink failure");
        Snapshots.Add(snapshot);
    }

    public void ReleaseAll() => ReleaseAllCount++;
}
