using System.Net;
using System.Net.Sockets;

namespace CTRL.Desktop.Transport;

/// <summary>
/// CTRL desktop TCP listener. Accepts inbound connections (client = CTRL
/// Mobile), wraps each in a <see cref="TcpConnection"/> and hands it to
/// <see cref="ClientConnected"/>. Default port 42123.
/// </summary>
public sealed class TcpServer
{
    public const int DefaultPort = 42123;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<TcpConnection> _connections = new();
    private readonly object _gate = new();
    private int _started;
    private int _stopped;

    public TcpServer(int port = DefaultPort, IPAddress? address = null)
    {
        if (port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        _listener = new TcpListener(address ?? IPAddress.Any, port);
    }

    /// <summary>Actual bound port; useful when 0 (ephemeral) is requested.</summary>
    public int LocalPort
    {
        get
        {
            if (Volatile.Read(ref _started) == 0)
                return -1;
            return ((IPEndPoint)_listener.LocalEndpoint).Port;
        }
    }

    public event Func<TcpConnection, Task>? ClientConnected;
    public event Action<string>? ServerError;

    /// <summary>
    /// Runs the accept loop until <see cref="StopAsync"/> is called. The loop
    /// never throws; listener failures are reported via <see cref="ServerError"/>.
    /// </summary>
    public async Task StartAsync()
    {
        _listener.Start();
        Volatile.Write(ref _started, 1);
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    if (!_cts.IsCancellationRequested)
                        ServerError?.Invoke($"Listener error: {ex.Message}");
                    break;
                }

                _ = RunClientAsync(client);
            }
        }
        finally
        {
            _listener.Stop();
            Volatile.Write(ref _started, 0);
        }
    }

    private async Task RunClientAsync(TcpClient client)
    {
        var connection = new TcpConnection(client);
        lock (_gate)
        {
            _connections.Add(connection);
        }

        try
        {
            if (ClientConnected is not null)
                await ClientConnected(connection).ConfigureAwait(false);
            await connection.RunAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _connections.Remove(connection);
            }
        }
    }

    /// <summary>Stops accepting new connections and closes all active ones.</summary>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;
        _cts.Cancel();

        TcpConnection[] pending;
        lock (_gate)
        {
            _listener.Stop();
            pending = _connections.ToArray();
        }

        foreach (var connection in pending)
            await connection.CloseAsync().ConfigureAwait(false);
    }
}
