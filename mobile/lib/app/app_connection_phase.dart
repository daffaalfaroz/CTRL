/// Application-level connection lifecycle for the CTRL mobile app (M3.0).
///
/// This is a UI-facing projection of the engine's `ClientSessionState` — it
/// deliberately does NOT duplicate the protocol state machine. Mapping lives
/// in `ConnectionController`.
enum AppConnectionPhase {
  /// No session/transport; the normal idle state.
  disconnected,

  /// Handshake running with a PAIRING CODE credential (first-time pairing).
  pairing,

  /// Handshake running with a stored TOKEN credential, or generic setup.
  connecting,

  /// Session READY: application-plane I/O allowed.
  connected,

  /// Unexpected drop reported by the engine (D8); manual reset offered.
  reconnecting,

  /// Terminal failure for the current attempt; message is safe for display.
  error,
}
