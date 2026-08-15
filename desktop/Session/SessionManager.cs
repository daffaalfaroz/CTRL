namespace CTRL.Desktop.Session;

/// <summary>
/// Tracks the active session per deviceId (docs/protocol.md §3: at most one
/// session per device). When a new session authenticates for a device that
/// already has an active session, the old one is taken over (D3).
/// </summary>
public sealed class SessionManager
{
    private readonly Dictionary<string, Session> _active = new();
    private readonly object _gate = new();

    /// <summary>(newSession, displacedOldSession)</summary>
    public event Action<Session, Session>? SessionReplaced;

    public void Register(Session session)
    {
        session.Authenticated += OnAuthenticated;
        session.Closed += OnClosed;
        if (session.State == ServerSessionState.Ready)
            OnAuthenticated(session);
    }

    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _active.Count;
            }
        }
    }

    public bool IsActive(string deviceId)
    {
        lock (_gate)
        {
            return _active.ContainsKey(deviceId);
        }
    }

    private void OnAuthenticated(Session session)
    {
        Session? displaced = null;
        lock (_gate)
        {
            if (_active.TryGetValue(session.DeviceId, out var existing) && existing != session)
                displaced = existing;
            _active[session.DeviceId] = session;
        }

        if (displaced is not null)
        {
            SessionReplaced?.Invoke(session, displaced);
            displaced.TerminateForTakeover();
        }
    }

    private void OnClosed(Session session)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(session.DeviceId, out var current) && current == session)
                _active.Remove(session.DeviceId);
        }
    }
}