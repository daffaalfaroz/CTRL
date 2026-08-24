import 'dart:convert';
import 'dart:typed_data';

import 'package:ctrl_mobile/crypto/hmac.dart';
import 'package:ctrl_mobile/session/authenticator.dart';
import 'package:ctrl_mobile/session/secure_token_store.dart';
import 'package:ctrl_mobile/session/token_store.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';

Uint8List _tokenOf(List<int> bytes) => Uint8List.fromList(bytes);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  group('InMemoryTokenStore', () {
    test('save then load returns the same bytes', () async {
      final store = InMemoryTokenStore();
      final token = _tokenOf([1, 2, 3, 0xFF]);
      await store.save('dev-1', token);
      expect(await store.load('dev-1'), orderedEquals(token));
    });

    test('load on an empty store returns null', () async {
      final store = InMemoryTokenStore();
      expect(await store.load('nobody'), isNull);
    });

    test('delete removes the token', () async {
      final store = InMemoryTokenStore();
      await store.save('dev-1', _tokenOf([9]));
      await store.delete('dev-1');
      expect(await store.load('dev-1'), isNull);
    });

    test('saving again replaces the token', () async {
      final store = InMemoryTokenStore();
      await store.save('dev-1', _tokenOf([1]));
      await store.save('dev-1', _tokenOf([2, 2]));
      expect(await store.load('dev-1'), orderedEquals(_tokenOf([2, 2])));
    });

    test('tokens for different deviceIds are isolated', () async {
      final store = InMemoryTokenStore();
      await store.save('dev-1', _tokenOf([1]));
      await store.save('dev-2', _tokenOf([2]));
      expect(await store.load('dev-1'), orderedEquals(_tokenOf([1])));
      expect(await store.load('dev-2'), orderedEquals(_tokenOf([2])));
    });
  });

  group('SecureTokenStore (platform channel mocked)', () {
    setUp(() {
      FlutterSecureStorage.setMockInitialValues(<String, String>{});
    });

    test('save then load round-trips raw binary token bytes', () async {
      final store = SecureTokenStore();
      final token =
          _tokenOf([0x00, 0x80, 0xFF, 0x10, 0xFE, 0x42, 0x00, 0x07]);
      await store.save('ctrl-42a8', token);
      expect(await store.load('ctrl-42a8'), orderedEquals(token));
    });

    test('load on an empty backing store returns null', () async {
      final store = SecureTokenStore();
      expect(await store.load('never-paired'), isNull);
    });

    test('delete makes the token unreadable', () async {
      final store = SecureTokenStore();
      await store.save('ctrl-42a8', _tokenOf([7, 8, 9]));
      await store.delete('ctrl-42a8');
      expect(await store.load('ctrl-42a8'), isNull);
    });

    test('saving again replaces the stored token', () async {
      final store = SecureTokenStore();
      await store.save('ctrl-42a8', _tokenOf([1]));
      await store.save('ctrl-42a8', _tokenOf([5, 5, 5]));
      expect(await store.load('ctrl-42a8'), orderedEquals(_tokenOf([5, 5, 5])));
    });

    test('corrupted entry self-heals to null instead of crashing', () async {
      const key = 'ctrl.token.ZGV2LTE='; // base64("dev-1")
      FlutterSecureStorage.setMockInitialValues(
          <String, String>{key: '%%%not-base64%%%'});
      final store = SecureTokenStore();
      expect(await store.load('dev-1'), isNull);
      // Self-healed: the broken entry was removed from the backing store.
      expect(await const FlutterSecureStorage().readAll(), isNot(contains(key)));
    });

    test('persistence across instances: save in A, read in B (app restart '
        'simulation)', () async {
      final firstInstance = SecureTokenStore();
      final issuedToken = _tokenOf(
          List.generate(32, (i) => (i * 11 + 3) & 0xFF)); // server-issued form
      await firstInstance.save('integration-device', issuedToken);

      // Simulates process death: a brand-new store instance over the same
      // platform-backed storage. With the real plugin this storage lives in
      // EncryptedSharedPreferences guarded by the Android Keystore.
      final secondInstance = SecureTokenStore();
      final loaded = await secondInstance.load('integration-device');
      expect(loaded, isNotNull);
      expect(loaded, orderedEquals(issuedToken));
    });
  });

  group('M1.4.5: token lifecycle into AUTH (§12)', () {
    test('stored token feeds HmacAuthenticator.token unchanged', () async {
      FlutterSecureStorage.setMockInitialValues(<String, String>{});
      final store = SecureTokenStore();
      final issuedByServer = Uint8List.fromList(
          List.generate(32, (i) => (i * 31 + 17) & 0xFF));
      await store.save('ctrl-restart', issuedByServer);

      // App restart: fresh authenticator built from what the store returns.
      final restored = (await store.load('ctrl-restart'))!;
      final auth =
          HmacAuthenticator.token(token: restored, deviceId: 'ctrl-restart');

      final challenge = Uint8List.fromList(List.generate(32, (i) => i));
      final expectedMac = hmacSha256(issuedByServer, challenge);
      expect(auth.challengeResponseFor(challenge), orderedEquals(expectedMac));
      // The wire credential stays empty for token auth (credentialLength=0).
      expect(auth.buildAuth(challenge).credential, isEmpty);
    });

    test('delete() prevents any further authentication with the old token',
        () async {
      FlutterSecureStorage.setMockInitialValues(<String, String>{});
      final store = SecureTokenStore();
      await store.save('unpair-me', _tokenOf(utf8.encode('old-token')));
      await store.delete('unpair-me');
      expect(await store.load('unpair-me'), isNull,
          reason: 'logout/unpair must leave nothing readable');
    });
  });
}
