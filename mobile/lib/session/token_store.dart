import 'dart:async';
import 'dart:typed_data';

/// Storage for the persistent device token issued via AUTH_OK newToken
/// (docs/protocol.md §12: "Client wajib menyimpan newToken dengan aman").
///
/// All operations are asynchronous so implementations can back onto real
/// secure storage (Android Keystore et al.) without blocking the UI isolate.
abstract class TokenStore {
  /// Persists [token] for [deviceId]. On success the exact bytes must be
  /// recoverable via [load]; failures surface as an errored future (callers
  /// must never fall back to plaintext storage).
  Future<void> save(String deviceId, Uint8List token);

  /// Returns the stored token for [deviceId], or null when absent/unreadable.
  /// Implementations must not fabricate tokens.
  Future<Uint8List?> load(String deviceId);

  /// Removes the token for [deviceId]. After completion [load] returns null.
  Future<void> delete(String deviceId);
}

/// In-memory [TokenStore]: no persistence, used by tests and the integration
/// harness. Production apps use [SecureTokenStore].
class InMemoryTokenStore implements TokenStore {
  final Map<String, Uint8List> _tokens = <String, Uint8List>{};

  @override
  Future<void> save(String deviceId, Uint8List token) async {
    _tokens[deviceId] = Uint8List.fromList(token);
  }

  @override
  Future<Uint8List?> load(String deviceId) async {
    final token = _tokens[deviceId];
    return token == null ? null : Uint8List.fromList(token);
  }

  @override
  Future<void> delete(String deviceId) async {
    _tokens.remove(deviceId);
  }
}

/// Runs [body], ignoring the result. Errors are reported through [onError]
/// (which must never log token material).
void unawaitedReported(Future<void> future, void Function(Object error) onError) {
  unawaited(future.catchError(onError));
}
