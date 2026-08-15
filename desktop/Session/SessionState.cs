namespace CTRL.Desktop.Session;

/// <summary>Server-side session lifecycle per docs/protocol.md §19.</summary>
public enum ServerSessionState
{
    /// <summary>Connection accepted, nothing sent yet.</summary>
    Connected,

    /// <summary>Waiting for the client's HELLO.</summary>
    WaitHello,

    /// <summary>WELCOME sent, waiting for AUTH.</summary>
    WaitAuth,

    /// <summary>AUTH_OK sent; application-plane input is allowed.</summary>
    Ready,

    /// <summary>Close initiated; waiting for transport teardown.</summary>
    Closing,

    /// <summary>Terminal state.</summary>
    Closed,
}