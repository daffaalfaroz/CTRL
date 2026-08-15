import '../protocol/input_snapshot_payload.dart';

/// Supplies the current full input state. The client sends an INPUT_SNAPSHOT
/// (>=1 entry) as its first application-plane message right after AUTH_OK and
/// re-sends it whenever the server issues INPUT_RESET.
abstract class InputSnapshotProvider {
  InputSnapshotPayload currentSnapshot();
}