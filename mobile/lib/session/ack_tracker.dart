/// Tracks outbound messages that requested an ACK and, on timeout, retries
/// exactly once (docs/protocol.md §7). Runs on an injected clock so it stays
/// fully deterministic in tests — no real timers in this milestone.
class AckTracker {
  AckTracker({
    required this.nowMs,
    required this.timeoutMs,
  });

  final int Function() nowMs;
  final int timeoutMs;

  final Map<int, _PendingAck> _pending = <int, _PendingAck>{};

  int get pendingCount => _pending.length;

  void track(int sequence) {
    if (!_pending.containsKey(sequence)) {
      _pending[sequence] = _PendingAck(sentAtMs: nowMs(), attempts: 0);
    }
  }

  void acknowledge(int sequence) => _pending.remove(sequence);

  /// Resets the deadline of a pending sequence after a retransmit, keeping its
  /// attempt count so the retry fires exactly once (§7).
  void reschedule(int sequence) {
    final pending = _pending[sequence];
    if (pending != null) {
      pending.sentAtMs = nowMs();
    }
  }

  bool isPending(int sequence) => _pending.containsKey(sequence);

  /// Sequences whose first deadline passed and still await an ACK. Each call
  /// returns each sequence at most once (it is marked as retried).
  List<int> retryExpired() {
    final now = nowMs();
    final expired = <int>[];
    for (final entry in _pending.entries) {
      final pending = entry.value;
      if (pending.attempts == 0 && now - pending.sentAtMs >= timeoutMs) {
        pending.attempts = 1;
        pending.sentAtMs = now;
        expired.add(entry.key);
      }
    }
    return expired;
  }

  /// Sequences still unacknowledged after the retry deadline (failed).
  List<int> failed() {
    final now = nowMs();
    final failed = <int>[];
    for (final entry in _pending.entries) {
      final pending = entry.value;
      if (pending.attempts >= 1 && now - pending.sentAtMs >= timeoutMs) {
        failed.add(entry.key);
      }
    }
    return failed;
  }
}

class _PendingAck {
  _PendingAck({required this.sentAtMs, required this.attempts});

  int sentAtMs;
  int attempts;
}