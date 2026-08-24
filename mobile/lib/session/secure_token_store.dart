import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'token_store.dart';

/// Persistent [TokenStore] backed by `flutter_secure_storage`.
///
/// On Android the plugin stores values through EncryptedSharedPreferences:
/// the AES encryption key is generated inside and guarded by the Android
/// Keystore, so tokens are encrypted at rest, never written to plain files or
/// SharedPreferences, and survive app restarts. Key invalidation (e.g. after
/// reinstall or device restore) surfaces as a read returning null — the app
/// then simply pairs again; no plaintext fallback exists anywhere.
class SecureTokenStore implements TokenStore {
  SecureTokenStore({FlutterSecureStorage? storage})
      : _storage = storage ??
            const FlutterSecureStorage(
              aOptions: AndroidOptions(encryptedSharedPreferences: true),
            );

  final FlutterSecureStorage _storage;

  /// Namespaces the per-device keys; deviceId itself never becomes a bare
  /// storage key (it is UTF-8 + base64 encoded to avoid delimiter issues).
  String _key(String deviceId) =>
      'ctrl.token.${base64Encode(utf8.encode(deviceId))}';

  @override
  Future<void> save(String deviceId, Uint8List token) {
    // base64 keeps the raw token bytes binary-safe inside the string-typed
    // store; confidentiality comes from the Keystore-backed encryption below
    // this API, not from the encoding.
    return _storage.write(key: _key(deviceId), value: base64Encode(token));
  }

  @override
  Future<Uint8List?> load(String deviceId) async {
    final value = await _storage.read(key: _key(deviceId));
    if (value == null || value.isEmpty) {
      return null;
    }
    try {
      return Uint8List.fromList(base64Decode(value));
    } on FormatException {
      // Corrupted entry (e.g. partial write): treat as missing — self-heal by
      // removing it instead of crashing, logging bytes, or inventing a token.
      await _storage.delete(key: _key(deviceId));
      return null;
    }
  }

  @override
  Future<void> delete(String deviceId) => _storage.delete(key: _key(deviceId));
}
