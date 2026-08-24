import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:ctrl_mobile/crypto/hmac.dart';
import 'package:ctrl_mobile/crypto/sha256.dart';

void main() {
  group('SHA-256 (FIPS 180-4 vectors)', () {
    test('empty message', () {
      expect(bytesToHex(sha256Bytes(const [])),
          'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855');
    });

    test('"abc"', () {
      expect(bytesToHex(sha256Bytes(utf8.encode('abc'))),
          'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
    });

    test('two-block message', () {
      expect(
          bytesToHex(sha256Bytes(utf8.encode(
              'abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq'))),
          '248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1');
    });

    test('one million "a" (multi-block stress)', () {
      final millionA = Uint8List(1000000)..fillRange(0, 1000000, 0x61);
      expect(bytesToHex(sha256Bytes(millionA)),
          'cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0');
    });
  });

  group('HMAC-SHA256 (RFC 4231 vectors)', () {
    test('case 1: short key, short data', () {
      final mac = hmacSha256(List.filled(20, 0x0b), utf8.encode('Hi There'));
      expect(bytesToHex(mac),
          'b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7');
    });

    test('case 2: named key and data', () {
      final mac = hmacSha256(utf8.encode('Jefe'),
          utf8.encode('what do ya want for nothing?'));
      expect(bytesToHex(mac),
          '5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843');
    });

    test('case 3: 20-byte key, 50-byte data', () {
      final mac =
          hmacSha256(List.filled(20, 0xaa), List.filled(50, 0xdd));
      expect(bytesToHex(mac),
          '773ea91e36800e46854db8ebd09181a72959098b3ef8c122d9635514ced565fe');
    });

    test('case 6: key larger than block size is hashed first', () {
      final mac = hmacSha256(
          List.filled(131, 0xaa),
          utf8.encode('Test Using Larger Than Block-Size Key - Hash Key First'));
      expect(bytesToHex(mac),
          '60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54');
    });

    test('case 7: larger key and data', () {
      final mac = hmacSha256(
          List.filled(131, 0xaa),
          utf8.encode('This is a test using a larger than block-size key '
              'and a larger than block-size data. The key needs to be hashed '
              'before being used by the HMAC algorithm.'));
      expect(bytesToHex(mac),
          '9b09ffa71b942fcb27635fbcd5b0e944bfdc63644f0713938a7f51535c3a35e2');
    });

    test('output length is exactly 32 bytes (§12 challengeResponse size)', () {
      expect(hmacSha256([1, 2, 3], [4, 5]).length, 32);
    });
  });

  group('Cross-language vector (pinned against C# HMACSHA256)', () {
    test('pairing secret over challenge 0x00..0x1f', () {
      final secret = utf8.encode('ctrl-m144-cross-vector-secret');
      final challenge = Uint8List.fromList(List.generate(32, (i) => i));
      expect(bytesToHex(hmacSha256(secret, challenge)),
          'ce7542e18060a6367f4b393b7203b929bc5b2875d0a17f0be67e71a49210a23f');
    });

    test('token secret over challenge 0x00..0x1f', () {
      final secret = utf8.encode('ctrl-m144-token-secret');
      final challenge = Uint8List.fromList(List.generate(32, (i) => i));
      expect(bytesToHex(hmacSha256(secret, challenge)),
          '6a074159523a97c4e184ee965814fe428ece623530925f54a5ea6194bdd18945');
    });
  });
}
