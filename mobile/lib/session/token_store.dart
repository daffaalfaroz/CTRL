import 'dart:typed_data';

/// Storage for the persistent device token issued via AUTH_OK newToken
/// (docs/protocol.md §12: "Client wajib menyimpan newToken dengan aman").
///
/// M1.4.4 ships the interface plus an in-memory implementation so sessions,
/// tests, and the integration harness can run without new dependencies.
/// Production persistence requires a secure store (Android Keystore-backed,
/// e.g. flutter_secure_storage) — adding that dependency is pending approval
/// and must NOT be a plain-text file.
abstract class TokenStore {
  void save(String deviceId, Uint8List token);

  Uint8List? load(String deviceId);
}

class InMemoryTokenStore implements TokenStore {
  final Map<String, Uint8List> _tokens = <String, Uint8List>{};

  @override
  void save(String deviceId, Uint8List token) {
    _tokens[deviceId] = Uint8List.fromList(token);
  }

  @override
  Uint8List? load(String deviceId) {
    final token = _tokens[deviceId];
    return token == null ? null : Uint8List.fromList(token);
  }
}
