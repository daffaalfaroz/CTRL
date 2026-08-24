using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Transport;

public interface ITransportConnection
{
    bool IsConnected { get; }

    /// <summary>Remote peer address (IP for TCP; informational for fakes).
    /// Used by the authenticator to scope auth-failure lockout (§12).</summary>
    string RemoteAddress { get; }

    event Action<ProtocolFrame>? FrameReceived;
    event Action<string>? Disconnected;

    Task SendAsync(byte[] frame, CancellationToken cancellationToken = default);
    Task CloseAsync();
}
