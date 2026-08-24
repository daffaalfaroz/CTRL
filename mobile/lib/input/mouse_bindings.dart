/// CTRL mouse binding convention shared by the mobile UI and the desktop
/// mapper (`desktop/Input/Win32/Win32InputMapper.cs`) — M2.2.
///
/// Wire-wise these are ordinary INPUT_EVENT records (§8/§9); the desktop owns
/// the binding to SendInput. Movement is RELATIVE velocity from a stick
/// (`analisis-teknis.md §15`), scroll uses two axis controls for direction.
library;

const String mouseLeftControlId = 'mouse:left';
const String mouseRightControlId = 'mouse:right';
const String mouseMiddleControlId = 'mouse:middle';

/// Stick control driving relative cursor velocity (-1..1 x/y).
const String mouseMoveControlId = 'mouse:move';

/// Axis controls (0..1) driving wheel notch rate per direction.
const String mouseWheelUpControlId = 'mouse:wheelup';
const String mouseWheelDownControlId = 'mouse:wheeldown';

/// The three mouse buttons recognized in M2.2.
const List<String> supportedMouseButtons = [
  mouseLeftControlId,
  mouseRightControlId,
  mouseMiddleControlId,
];
