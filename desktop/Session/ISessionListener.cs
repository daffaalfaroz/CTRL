using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Session;

/// <summary>Receives session lifecycle events and inbound input records.</summary>
public interface ISessionListener
{
    void OnStateChanged(ServerSessionState state);
    void OnError(string message);
    void OnInputEvent(InputEvent inputEvent);
    void OnInputSnapshot(InputSnapshotPayload snapshot);
}

/// <summary>Flushes pending input state when a session ends (docs/protocol.md
/// §13, §21.6). Implementations must be idempotent and cheap.</summary>
public interface IInputStateFlusher
{
    void Flush();
}