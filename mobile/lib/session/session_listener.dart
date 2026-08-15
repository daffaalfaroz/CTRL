import '../protocol/error_payload.dart';
import '../protocol/pong_payload.dart';
import 'session_state.dart';

/// Receives client-session lifecycle events.
abstract class SessionListener {
  void onStateChanged(ClientSessionState state);

  /// Non-fatal diagnostics (protocol/log bookkeeping).
  void onError(String message);

  /// A fatal ERROR from the server (severity fatal).
  void onFatalError(ErrorPayload error);

  void onPong(PongPayload pong);
}