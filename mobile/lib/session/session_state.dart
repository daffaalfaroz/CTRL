/// Client-side session lifecycle per docs/protocol.md §19.
enum ClientSessionState {
  /// No transport connection.
  closed,

  /// Transport connect in progress (or about to start on the established
  /// transport). Transient; the next state is [connected].
  connecting,

  /// Transport connected; HELLO not yet sent.
  connected,

  /// HELLO sent; waiting for WELCOME.
  waitWelcome,

  /// WELCOME received; AUTH not yet sent.
  waitAuth,

  /// AUTH sent; waiting for AUTH_OK.
  waitAuthOk,

  /// AUTH_OK received; application-plane I/O allowed.
  ready,

  /// Connection lost unexpectedly; reconnect() is allowed (manual only, D8).
  reconnecting,

  /// Deliberately disconnected or rejected (AUTH_DENIED / fatal ERROR).
  disconnected,
}