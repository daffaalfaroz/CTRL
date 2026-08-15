/// Continuous per-direction sequence counter (D1: no reset after AUTH_OK, per
/// docs/protocol.md §19). Wraps modulo 2^16.
class SequenceTracker {
  int _next = 0;

  int next() {
    final value = _next;
    _next = (_next + 1) & 0xFFFF;
    return value;
  }

  int get current => _next;
}

/// Tracks the last received sequence in one direction and validates that the
/// next one is monotonically increasing modulo 2^16 (delta 1..0x7FFF).
/// Failures are reported by the session but do not tear down the connection.
class InboundSequenceTracker {
  bool _hasLast = false;
  int _last = 0;

  bool isMonotonic(int sequence) {
    if (!_hasLast) {
      _last = sequence;
      _hasLast = true;
      return true;
    }
    final delta = (sequence - _last) & 0xFFFF;
    _last = sequence;
    return delta != 0 && delta <= 0x7FFF;
  }
}