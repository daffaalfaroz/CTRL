import 'dart:typed_data';

import 'sha256.dart';

/// HMAC-SHA256 per RFC 2104 (docs/protocol.md §12: challengeResponse =
/// HMAC-SHA256(sharedSecret, challenge), no KDF in v1). Verified against the
/// RFC 4231 vectors and cross-language vectors shared with the C# suite.
Uint8List hmacSha256(List<int> key, List<int> data) {
  var k = Uint8List.fromList(key);
  if (k.length > 64) {
    k = sha256Bytes(k);
  }
  final block = Uint8List(64);
  block.setRange(0, k.length, k);

  final ipad = Uint8List(64);
  final opad = Uint8List(64);
  for (var i = 0; i < 64; i++) {
    ipad[i] = block[i] ^ 0x36;
    opad[i] = block[i] ^ 0x5c;
  }

  final innerInput = Uint8List(64 + data.length);
  innerInput.setRange(0, 64, ipad);
  innerInput.setRange(64, 64 + data.length, data);
  final inner = sha256Bytes(innerInput);

  final outerInput = Uint8List(64 + 32);
  outerInput.setRange(0, 64, opad);
  outerInput.setRange(64, 96, inner);
  return sha256Bytes(outerInput);
}
