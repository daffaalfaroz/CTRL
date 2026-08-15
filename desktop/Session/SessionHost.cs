using CTRL.Desktop.Transport;

namespace CTRL.Desktop.Session;

/// <summary>
/// Wires <see cref="TcpServer"/> accept events to fresh <see cref="Session"/>
/// instances, registering each with a shared <see cref="SessionManager"/>
/// (which enforces one-session-per-device, D3 takeover).
/// </summary>
public sealed class SessionHost
{
    private readonly TcpServer _server;
    private readonly IAuthenticator _authenticator;
    private readonly ISessionListener _listener;
    private readonly IInputStateFlusher? _flusher;
    private readonly SessionOptions _options;
    private readonly SessionManager _manager = new();

    public event Action<Session>? SessionAccepted;
    public event Action<Session, Session>? SessionReplaced;

    public SessionHost(
        TcpServer server,
        IAuthenticator authenticator,
        ISessionListener listener,
        SessionOptions? options = null,
        IInputStateFlusher? flusher = null)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _flusher = flusher;
        _options = options ?? new SessionOptions();
        _server.ClientConnected += OnClientConnected;
        _manager.SessionReplaced += (newSession, oldSession) => SessionReplaced?.Invoke(newSession, oldSession);
    }

    public SessionManager Manager => _manager;
    public bool IsListening => _server.LocalPort >= 0;

    public Task StartAsync() => _server.StartAsync();
    public Task StopAsync() => _server.StopAsync();

    private Task OnClientConnected(TcpConnection connection)
    {
        var session = new Session(connection, _authenticator, _listener, _options, _flusher);
        _manager.Register(session);
        SessionAccepted?.Invoke(session);
        return Task.CompletedTask;
    }
}