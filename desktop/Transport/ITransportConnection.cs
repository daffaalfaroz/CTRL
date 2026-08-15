using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Transport;

public interface ITransportConnection
{
    bool IsConnected { get; }

    event Action<ProtocolFrame>? FrameReceived;
    event Action<string>? Disconnected;

    Task SendAsync(byte[] frame, CancellationToken cancellationToken = default);
    Task CloseAsync();
}
