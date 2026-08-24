import 'dart:typed_data';

/// Pure-Dart SHA-256 (FIPS 180-4). CTRL avoids new pub dependencies, so the
/// hash primitive lives here; correctness is pinned by official test vectors
/// in test/crypto/sha256_test.dart.
const List<int> _k = [
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1,
  0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
  0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786,
  0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147,
  0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
  0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b,
  0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a,
  0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
  0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
];

int _rotr(int x, int n) => ((x >> n) | (x << (32 - n))) & 0xFFFFFFFF;

/// Computes the SHA-256 digest of [data].
Uint8List sha256Bytes(List<int> data) {
  final bitLength = data.length * 8;
  final paddedLength = ((data.length + 9 + 63) ~/ 64) * 64;
  final msg = Uint8List(paddedLength);
  msg.setRange(0, data.length, data);
  msg[data.length] = 0x80;
  final bd = ByteData.view(msg.buffer);
  bd.setUint64(paddedLength - 8, bitLength, Endian.big);

  var h0 = 0x6a09e667;
  var h1 = 0xbb67ae85;
  var h2 = 0x3c6ef372;
  var h3 = 0xa54ff53a;
  var h4 = 0x510e527f;
  var h5 = 0x9b05688c;
  var h6 = 0x1f83d9ab;
  var h7 = 0x5be0cd19;

  final w = Uint32List(64);
  for (var offset = 0; offset < paddedLength; offset += 64) {
    for (var t = 0; t < 16; t++) {
      w[t] = bd.getUint32(offset + t * 4, Endian.big);
    }
    for (var t = 16; t < 64; t++) {
      final s0 =
          _rotr(w[t - 15], 7) ^ _rotr(w[t - 15], 18) ^ ((w[t - 15] >>> 3));
      final s1 =
          _rotr(w[t - 2], 17) ^ _rotr(w[t - 2], 19) ^ ((w[t - 2] >>> 10));
      w[t] = (w[t - 16] + s0 + w[t - 7] + s1) & 0xFFFFFFFF;
    }

    var a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, hh = h7;
    for (var t = 0; t < 64; t++) {
      final s1 = _rotr(e, 6) ^ _rotr(e, 11) ^ _rotr(e, 25);
      final ch = (e & f) ^ (~e & g);
      final temp1 = (hh + s1 + ch + _k[t] + w[t]) & 0xFFFFFFFF;
      final s0 = _rotr(a, 2) ^ _rotr(a, 13) ^ _rotr(a, 22);
      final maj = (a & b) ^ (a & c) ^ (b & c);
      final temp2 = (s0 + maj) & 0xFFFFFFFF;
      hh = g;
      g = f;
      f = e;
      e = (d + temp1) & 0xFFFFFFFF;
      d = c;
      c = b;
      b = a;
      a = (temp1 + temp2) & 0xFFFFFFFF;
    }

    h0 = (h0 + a) & 0xFFFFFFFF;
    h1 = (h1 + b) & 0xFFFFFFFF;
    h2 = (h2 + c) & 0xFFFFFFFF;
    h3 = (h3 + d) & 0xFFFFFFFF;
    h4 = (h4 + e) & 0xFFFFFFFF;
    h5 = (h5 + f) & 0xFFFFFFFF;
    h6 = (h6 + g) & 0xFFFFFFFF;
    h7 = (h7 + hh) & 0xFFFFFFFF;
  }

  final out = Uint8List(32);
  final obd = ByteData.view(out.buffer);
  for (var i = 0; i < 8; i++) {
    obd.setUint32(i * 4, [h0, h1, h2, h3, h4, h5, h6, h7][i], Endian.big);
  }
  return out;
}

/// Lowercase hexadecimal rendering of [bytes].
String bytesToHex(List<int> bytes) {
  const digits = '0123456789abcdef';
  final sb = StringBuffer();
  for (final b in bytes) {
    sb.write(digits[(b >> 4) & 0xF]);
    sb.write(digits[b & 0xF]);
  }
  return sb.toString();
}
