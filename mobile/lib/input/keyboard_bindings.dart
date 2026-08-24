/// CTRL keyboard binding convention shared by the mobile UI and the desktop
/// mapper (`desktop/Input/Win32/Win32InputMapper.cs` — keep both tables in
/// sync).
///
/// docs/protocol.md leaves `controlId` semantic and delegates the binding to
/// the desktop (§18 intro); this file is the application-level convention both
/// sides agree on. It is NOT a wire-format change.
///
/// ControlIds use the form `key:<NAME>` where NAME is one of [supportedKeys],
/// case-insensitive on the desktop side.
library;

const String _keyPrefix = 'key:';

/// Canonical key names accepted by the CTRL keyboard binding.
/// Values are Windows virtual-key codes (documentation/tests only — the
/// desktop resolves names itself).
const Map<String, int> kVirtualKeys = {
  'SPACE': 0x20,
  'BACKSPACE': 0x08,
  'TAB': 0x09,
  'ENTER': 0x0D,
  'SHIFT': 0x10,
  'CONTROL': 0x11,
  'ALT': 0x12,
  'ESCAPE': 0x1B,
  'PRIOR': 0x21,
  'NEXT': 0x22,
  'END': 0x23,
  'HOME': 0x24,
  'LEFT': 0x25,
  'UP': 0x26,
  'RIGHT': 0x27,
  'DOWN': 0x28,
  'INSERT': 0x2D,
  'DELETE': 0x2E,
  'LSHIFT': 0xA0,
  'RSHIFT': 0xA1,
  'LCONTROL': 0xA2,
  'RCONTROL': 0xA3,
  'LMENU': 0xA4,
  'RMENU': 0xA5,
  'LWIN': 0x5B,
  'RWIN': 0x5C,
  'CAPITAL': 0x14,
  'NUMLOCK': 0x90,
  'SCROLL': 0x91,
  'PRINTSCREEN': 0x2C,
  'APPS': 0x5D,
};

/// Builds the canonical controlId for a named key.
///
/// Throws [ArgumentError] for unknown names so typos fail loudly in tests.
String keyboardControlId(String keyName) {
  final upper = keyName.toUpperCase();
  if (!isSupportedKeyName(upper)) {
    throw ArgumentError.value(keyName, 'keyName', 'unknown CTRL key name');
  }
  return '$_keyPrefix$upper';
}

/// Letters A..Z and digits 0..9 are valid key names by convention.
bool isSupportedKeyName(String keyName) {
  final upper = keyName.toUpperCase();
  if (kVirtualKeys.containsKey(upper)) {
    return true;
  }
  if (upper.length == 1) {
    final code = upper.codeUnitAt(0);
    return (code >= 0x41 && code <= 0x5A) || (code >= 0x30 && code <= 0x39);
  }
  // Function keys F1..F24.
  if (upper.length >= 2 && upper.startsWith('F')) {
    final n = int.tryParse(upper.substring(1));
    return n != null && n >= 1 && n <= 24;
  }
  return false;
}
