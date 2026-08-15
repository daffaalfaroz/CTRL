/// CTRL message type constants (docs/protocol.md §6). Mirrors the desktop
/// `MessageType` class so both sides share the same table.
class MessageType {
  MessageType._();

  static const int hello = 0x01;
  static const int welcome = 0x02;
  static const int auth = 0x03;
  static const int authOk = 0x04;
  static const int authDenied = 0x05;
  static const int inputEvent = 0x06;
  static const int inputSnapshot = 0x07;
  static const int inputReset = 0x08;
  static const int heartbeat = 0x09;
  static const int pong = 0x0A;
  static const int ack = 0x0B;
  static const int error = 0x0C;
  static const int disconnect = 0x0D;
  static const int profileListReq = 0x0E;
  static const int profileList = 0x0F;
  static const int profileSelect = 0x10;
  static const int profileSelected = 0x11;
  static const int configPush = 0x12;
  static const int status = 0x13;
  static const int gamepadStatus = 0x14;

  /// Input-plane messages carry input records; malformed input is dropped
  /// (log) instead of closing the connection (§18, §21.7).
  static bool isInputPlane(int type) =>
      type == inputEvent || type == inputSnapshot;

  /// Wajib-dipahami per docs/protocol.md §6: unknown receivers must answer
  /// ERROR unsupported-message + close when these are sent.
  static bool isMustUnderstand(int type) =>
      type == hello ||
      type == welcome ||
      type == auth ||
      type == authOk ||
      type == authDenied ||
      type == error ||
      type == disconnect ||
      type == profileListReq ||
      type == profileList ||
      type == profileSelect ||
      type == profileSelected ||
      type == configPush;
}