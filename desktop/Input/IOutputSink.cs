using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Input;

/// <summary>
/// Output boundary between the session layer and the platform input
/// implementation (docs/protocol.md §18: the desktop performs all
/// controlId → output mapping; the session never touches Win32).
///
/// Implementations:
///   - must be cheap and non-blocking (called on the socket read path);
///   - must treat <see cref="ReleaseAll"/> as "return every virtual output to
///     neutral" (§16) and make it idempotent;
///   - must not throw: the session guards calls, but a throwing sink is a bug.
/// </summary>
public interface IOutputSink
{
    /// <summary>Applies one decoded INPUT_EVENT from the active session.</summary>
    void HandleInput(InputEvent input);

    /// <summary>
    /// Replaces the full virtual-control state with the snapshot carried by
    /// INPUT_SNAPSHOT (docs/protocol.md §15).
    /// </summary>
    void HandleSnapshot(InputSnapshotPayload snapshot);

    /// <summary>Returns all outputs to neutral (§16 flush semantics).</summary>
    void ReleaseAll();
}
