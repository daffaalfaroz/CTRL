import 'package:flutter/material.dart';

import 'app/app_connection_phase.dart';
import 'app/connection_controller.dart';
import 'input/keyboard_panel.dart';
import 'session/secure_token_store.dart';
import 'session/token_store.dart';

void main() {
  runApp(const CtrlApp());
}

class CtrlApp extends StatelessWidget {
  const CtrlApp({super.key, this.tokenStore});

  /// Injectable for widget tests; production uses SecureTokenStore.
  final TokenStore? tokenStore;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'CTRL',
      theme: ThemeData(colorSchemeSeed: Colors.indigo, useMaterial3: true),
      home: ConnectionPage(tokenStore: tokenStore),
    );
  }
}

/// M3.0 Phase 1 application shell: connection lifecycle, status presentation
/// and a minimal control surface. Widgets only talk to
/// [ConnectionController] — protocol/session/security types stay out of the UI.
class ConnectionPage extends StatefulWidget {
  const ConnectionPage({super.key, this.tokenStore, this.controller});

  final TokenStore? tokenStore;
  final ConnectionController? controller;

  @override
  State<ConnectionPage> createState() => _ConnectionPageState();
}

class _ConnectionPageState extends State<ConnectionPage> {
  late ConnectionController _controller;
  late final TextEditingController _hostField;
  late final TextEditingController _portField;
  late final TextEditingController _deviceIdField;
  late final TextEditingController _pairingCodeField;
  bool _hasStoredToken = false;

  static const List<String> _quickKeys = [
    'SPACE',
    'ENTER',
    'ESCAPE',
    'TAB',
    'UP',
    'DOWN',
    'LEFT',
    'RIGHT',
    'LCONTROL',
    'LSHIFT',
  ];

  @override
  void initState() {
    super.initState();
    _controller = widget.controller ??
        ConnectionController(tokenStore: widget.tokenStore ?? SecureTokenStore());
    _controller.addListener(_onControllerChanged);
    _hostField = TextEditingController(text: '127.0.0.1');
    _portField = TextEditingController(text: '42123');
    _deviceIdField = TextEditingController(text: 'ctrl-android');
    _pairingCodeField = TextEditingController();
    _refreshStoredTokenFlag();
  }

  @override
  void didUpdateWidget(covariant ConnectionPage oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!identical(oldWidget.controller, widget.controller)) {
      final old = _controller;
      old.removeListener(_onControllerChanged);
      old.dispose();
      _controller = widget.controller ??
          ConnectionController(
              tokenStore: widget.tokenStore ?? SecureTokenStore());
      _controller.addListener(_onControllerChanged);
      _refreshStoredTokenFlag();
    }
  }

  Future<void> _refreshStoredTokenFlag() async {
    final has = await _controller.hasStoredToken(_deviceIdField.text.trim());
    if (mounted && has != _hasStoredToken) {
      setState(() => _hasStoredToken = has);
    }
  }

  void _onControllerChanged() {
    if (mounted) {
      setState(() {});
    }
  }

  @override
  void dispose() {
    _controller.removeListener(_onControllerChanged);
    _controller.dispose();
    _hostField.dispose();
    _portField.dispose();
    _deviceIdField.dispose();
    _pairingCodeField.dispose();
    super.dispose();
  }

  Future<void> _connect() async {
    await _refreshStoredTokenFlag();
    await _controller.connect(
      host: _hostField.text.trim(),
      port: int.tryParse(_portField.text.trim()) ?? 0,
      deviceId: _deviceIdField.text.trim(),
      pairingCode: _hasStoredToken ? null : _pairingCodeField.text.trim(),
    );
    await _refreshStoredTokenFlag();
  }

  @override
  Widget build(BuildContext context) {
    final phase = _controller.phase;
    final connected = phase == AppConnectionPhase.connected;

    return Scaffold(
      appBar: AppBar(title: const Text('CTRL')),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: ListView(
          children: [
            _StatusCard(phase: phase, error: _controller.lastError),
            const SizedBox(height: 16),
            TextField(
              controller: _hostField,
              enabled: !connected,
              decoration: const InputDecoration(labelText: 'Desktop address'),
            ),
            TextField(
              controller: _portField,
              enabled: !connected,
              keyboardType: TextInputType.number,
              decoration: const InputDecoration(labelText: 'Port'),
            ),
            TextField(
              controller: _deviceIdField,
              enabled: !connected,
              onChanged: (_) => _refreshStoredTokenFlag(),
              decoration:
                  const InputDecoration(labelText: 'Device ID'),
            ),
            if (!_hasStoredToken)
              TextField(
                controller: _pairingCodeField,
                enabled: !connected,
                obscureText: true,
                decoration: const InputDecoration(
                  labelText: 'Pairing code (shown on desktop)',
                ),
              ),
            const SizedBox(height: 12),
            Row(children: [
              Expanded(
                child: FilledButton(
                  onPressed: connected ? null : _connect,
                  child: Text(_hasStoredToken ? 'Connect' : 'Pair & connect'),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: OutlinedButton(
                  onPressed: connected ||
                          phase == AppConnectionPhase.reconnecting ||
                          phase == AppConnectionPhase.error
                      ? _resetOrDisconnect
                      : null,
                  child: const Text('Disconnect'),
                ),
              ),
            ]),
            if (connected) ...[
              const SizedBox(height: 20),
              Text('Keyboard controls',
                  style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: 8),
              KeyboardPanel(
                keys: _quickKeys,
                onKeyEvent: (controlId, down) =>
                    _controller.sendKeyEvent(controlId, down),
              ),
            ],
          ],
        ),
      ),
    );
  }

  Future<void> _resetOrDisconnect() async {
    if (_controller.phase == AppConnectionPhase.connected) {
      await _controller.disconnect();
    } else {
      // Reconnecting/error states: tear down and return to setup (D8 manual
      // reconnect creates a brand-new session via Connect).
      await _controller.resetAfterFailure();
    }
    await _refreshStoredTokenFlag();
  }
}

class _StatusCard extends StatelessWidget {
  const _StatusCard({required this.phase, required this.error});

  final AppConnectionPhase phase;
  final String? error;

  @override
  Widget build(BuildContext context) {
    final (label, color, icon) = switch (phase) {
      AppConnectionPhase.disconnected => ('Disconnected', Colors.grey, Icons.link_off),
      AppConnectionPhase.pairing => ('Pairing…', Colors.orange, Icons.key),
      AppConnectionPhase.connecting => ('Connecting…', Colors.orange, Icons.sync),
      AppConnectionPhase.connected => ('Connected', Colors.green, Icons.check_circle),
      AppConnectionPhase.reconnecting => ('Reconnecting…', Colors.orange, Icons.autorenew),
      AppConnectionPhase.error => ('Error', Colors.red, Icons.error),
    };

    return Card(
      color: color.withValues(alpha: 0.15),
      child: ListTile(
        leading: Icon(icon, color: color),
        title: Text(label,
            style: TextStyle(fontWeight: FontWeight.bold, color: color)),
        subtitle: error == null ? null : Text(error!),
      ),
    );
  }
}
