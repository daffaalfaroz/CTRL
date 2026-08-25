import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// User-facing connection settings persisted between launches (M3.0 Phase 2).
///
/// Deliberately EXCLUDES secrets: pairing codes and auth tokens are handled
/// exclusively by the engine's TokenStore/SecureTokenStore architecture.
/// Storage reuses the already-present flutter_secure_storage dependency —
/// no new packages.
class ConnectionSettings {
  const ConnectionSettings({
    required this.host,
    required this.port,
    required this.deviceId,
  });

  static const ConnectionSettings defaults = ConnectionSettings(
    host: '127.0.0.1',
    port: 42123,
    deviceId: 'ctrl-android',
  );

  final String host;
  final int port;
  final String deviceId;

  /// Validates and repairs values; malformed input falls back to safe
  /// defaults field-by-field without throwing (M3.0 requirement).
  factory ConnectionSettings.fromJson(Map<String, dynamic> json) {
    const fallback = defaults;
    final host = (json['host'] as String?)?.trim() ?? '';
    var port = json['port'];
    if (port is String) {
      port = int.tryParse(port);
    }
    final deviceId = (json['deviceId'] as String?)?.trim() ?? '';
    return ConnectionSettings(
      host: host.isEmpty ? fallback.host : host,
      port: port is int && port > 0 && port <= 65535 ? port : fallback.port,
      deviceId: deviceId.isEmpty || deviceId.length > 64
          ? fallback.deviceId
          : deviceId,
    );
  }

  Map<String, dynamic> toJson() => {'host': host, 'port': port, 'deviceId': deviceId};

  @override
  bool operator ==(Object other) =>
      other is ConnectionSettings &&
      other.host == host &&
      other.port == port &&
      other.deviceId == deviceId;

  @override
  int get hashCode => Object.hash(host, port, deviceId);
}

/// Persists [ConnectionSettings] via the already-present flutter_secure_storage
/// plugin. Corrupt or missing payloads load as [ConnectionSettings.defaults].
class ConnectionSettingsStore {
  ConnectionSettingsStore({FlutterSecureStorage? storage})
      : _storage = storage ??
            const FlutterSecureStorage(
              aOptions: AndroidOptions(encryptedSharedPreferences: true),
            );

  static const String _key = 'ctrl.connection.settings';

  final FlutterSecureStorage _storage;

  Future<ConnectionSettings> load() async {
    final raw = await _storage.read(key: _key);
    if (raw == null || raw.isEmpty) {
      return ConnectionSettings.defaults;
    }
    try {
      return ConnectionSettings.fromJson(
          jsonDecode(raw) as Map<String, dynamic>);
    } on FormatException {
      return ConnectionSettings.defaults;
    } on TypeError {
      return ConnectionSettings.defaults;
    }
  }

  Future<void> save(ConnectionSettings settings) =>
      _storage.write(key: _key, value: jsonEncode(settings.toJson()));

  Future<void> clear() => _storage.delete(key: _key);
}
