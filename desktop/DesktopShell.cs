using CTRL.Desktop.Session;
using CTRL.Desktop.Settings;
using CTRL.Desktop.Transport;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace CTRL.Desktop;

public sealed class DesktopShell : IDisposable
{
    private readonly DesktopSettings _settings;
    private readonly PairingCodeService _pairingCodeService;
    private TcpServer? _server;
    private bool _isRunning;
    private bool _isListening;
    private ulong _localPort;
    private string _pairingCode = string.Empty;
    private int _clientCount;
    private readonly ConcurrentQueue<string> _logMessages = new();
    private readonly object _lock = new();

    public bool IsRunning
    {
        get { lock (_lock) return _isRunning; }
        private set { lock (_lock) { if (_isRunning == value) return; _isRunning = value; } }
    }

    public bool IsListening
    {
        get { lock (_lock) return _isListening; }
        private set { lock (_lock) { if (_isListening == value) return; _isListening = value; } }
    }

    public ulong? LocalPort
    {
        get { lock (_lock) return _localPort > 0 ? (ulong?)_localPort : null; }
        private set { lock (_lock) _localPort = value ?? 0; }
    }

    public int ClientCount
    {
        get { lock (_lock) return _clientCount; }
        private set { lock (_lock) { _clientCount = value; } }
    }

    public string? PairingCode
    {
        get { lock (_lock) return string.IsNullOrEmpty(_pairingCode) ? null : _pairingCode; }
        private set { lock (_lock) _pairingCode = value ?? string.Empty; }
    }

    public ConcurrentQueue<string> LogMessages => _logMessages;

    public DesktopShell()
    {
        _settings = DesktopSettingsStore.Load();
        _pairingCodeService = new PairingCodeService();
        _server = new TcpServer(_settings.ListenPort);
        _server.ClientConnected += OnClientConnected;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            IsRunning = true;
            IsListening = false;
            _clientCount = 0;
            _pairingCode = string.Empty;
            _logMessages.Clear();

            var port = _settings.ListenPort > 0 ? _settings.ListenPort : 0;
            _server = new TcpServer(port);
            _server.ClientConnected += OnClientConnected;

            PairingCode = _pairingCodeService.Issue();
            LocalPort = (ulong)(port > 0 ? port : 0);
            AddLog("DesktopShell started; port=" + port);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;

            _server?.StopAsync().GetAwaiter().GetResult();
            _server = null;

            IsListening = false;
            _clientCount = 0;
            _localPort = 0;
            _pairingCode = string.Empty;
            _logMessages.Clear();
            AddLog("DesktopShell stopped.");
        }
    }

    public void GeneratePairingCode()
    {
        lock (_lock)
        {
            PairingCode = _pairingCodeService.Issue();
            AddLog("Pairing code generated.");
        }
    }

    public void Dispose()
    {
        Stop();
        DesktopSettingsStore.Save(_settings);
    }

    private Task OnClientConnected(TcpConnection connection)
    {
        lock (_lock)
        {
            _clientCount++;
            AddLog("Client connected; count=" + _clientCount);
        }
        return Task.CompletedTask;
    }

    private void AddLog(string message)
    {
        _logMessages.Enqueue(message);
    }
}