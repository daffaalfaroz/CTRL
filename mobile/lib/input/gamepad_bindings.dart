/// CTRL abstract-gamepad binding convention shared by the mobile UI and the
/// desktop mapper (`desktop/Input/Win32/Win32InputMapper.cs`) — M2.4.
///
/// The protocol models gamepads ABSTRACTLY (CAP_GAMEPAD §18): buttons ride on
/// kind=button, sticks on kind=stick (x/y -1..1), triggers on kind=trigger
/// (0..1) and the D-pad on kind=hat (0..8). The desktop owns any binding to
/// real outputs; these ids are application-level conventions, not wire rules.
library;

const String gpA = 'gamepad:a';
const String gpB = 'gamepad:b';
const String gpX = 'gamepad:x';
const String gpY = 'gamepad:y';
const String gpLeftShoulder = 'gamepad:lb';
const String gpRightShoulder = 'gamepad:rb';
const String gpBack = 'gamepad:back';
const String gpStart = 'gamepad:start';
const String gpLeftStickClick = 'gamepad:lsclick';
const String gpRightStickClick = 'gamepad:rsclick';

const String gpLeftStick = 'gamepad:lstick';
const String gpRightStick = 'gamepad:rstick';
const String gpLeftTrigger = 'gamepad:lt';
const String gpRightTrigger = 'gamepad:rt';
const String gpDpad = 'gamepad:dpad';

/// All face/shoulder/system/stick-click button controlIds (kind = button).
const List<String> supportedGamepadButtons = [
  gpA,
  gpB,
  gpX,
  gpY,
  gpLeftShoulder,
  gpRightShoulder,
  gpBack,
  gpStart,
  gpLeftStickClick,
  gpRightStickClick,
];

/// D-pad hat values per docs/protocol.md §9.
const int dpadCenter = 0;
const int dpadUp = 1;
const int dpadUpRight = 2;
const int dpadRight = 3;
const int dpadDownRight = 4;
const int dpadDown = 5;
const int dpadDownLeft = 6;
const int dpadLeft = 7;
const int dpadUpLeft = 8;
