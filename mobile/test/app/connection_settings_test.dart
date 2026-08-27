import 'dart:convert';

import 'package:ctrl_mobile/app/connection_settings.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  group('ConnectionSettings.fromJson validation', () {
    test('valid payload round-trips', () {
      const settings =
          ConnectionSettings(host: '10.0.0.5', port: 1234, deviceId: 'dev-1');
      final restored = ConnectionSettings.fromJson(settings.toJson());
      expect(restored, settings);
    });

    test('invalid port falls back to default', () {
      final s = ConnectionSettings.fromJson(
          {'host': 'h', 'port': 99999, 'deviceId': 'd'});
      expect(s.port, ConnectionSettings.defaults.port);
      final s2 = ConnectionSettings.fromJson(
          {'host': 'h', 'port': 'not-a-number', 'deviceId': 'd'});
      expect(s2.port, ConnectionSettings.defaults.port);
    });

    test('empty host/deviceId fall back to defaults', () {
      final s = ConnectionSettings.fromJson({'host': '', 'port': 1, 'deviceId': ''});
      expect(s.host, ConnectionSettings.defaults.host);
      expect(s.deviceId, ConnectionSettings.defaults.deviceId);
    });
  });

  group('ConnectionSettingsStore (platform channel mocked)', () {
    setUp(() {
      FlutterSecureStorage.setMockInitialValues(<String, String>{});
    });

    test('empty backing store loads defaults', () async {
      final store = ConnectionSettingsStore();
      final loaded = await store.load();
      expect(loaded, ConnectionSettings.defaults);
    });

    test('save then load round-trips', () async {
      final store = ConnectionSettingsStore();
      const saved =
          ConnectionSettings(host: '192.168.1.50', port: 50000, deviceId: 'phone');
      await store.save(saved);
      expect(await store.load(), saved);
    });

    test('corrupt JSON falls back to defaults without crashing', () async {
      const key = 'ctrl.connection.settings';
      FlutterSecureStorage.setMockInitialValues(
          <String, String>{key: '{not valid json'});
      final store = ConnectionSettingsStore();
      final loaded = await store.load();
      expect(loaded, ConnectionSettings.defaults);
    });

    test('pairing code / token fields are never part of settings', () {
      // Guard against scope creep: secrets belong to TokenStore only.
      final json = ConnectionSettings.defaults.toJson();
      expect(json.containsKey('pairingCode'), isFalse);
      expect(json.containsKey('token'), isFalse);
      expect(const JsonEncoder().convert(json).contains('token'), isFalse);
    });
  });
}
