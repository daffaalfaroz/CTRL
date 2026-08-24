using System.Net.Sockets;
using CTRL.Desktop.Protocol;

namespace CTRL.Desktop.Transport;

/// <summary>
/// A single TCP connection with CTRL framing. Applies TCP_NODELAY, runs a
/// read loop that reassembles frames via <see cref="FrameBuffer"/>, decodes
/// them with <see cref="FrameCodec"/>, and reports disconnect exactly once.
/// </summary>
public sealed class TcpConnection : ITransportConnection
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly FrameBuffer _frameBuffer = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _closed;

    public TcpConnection(TcpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        client.NoDelay = true;
        _stream = client.GetStream();
        try
        {
            RemoteAddress =
                (_client.Client.RemoteEndPoint as System.Net.IPEndPoint)?
                    .Address.ToString() ?? string.Empty;
        }
        catch
        {
            RemoteAddress = string.Empty;
        }
    }

    public bool IsConnected => Volatile.Read(ref _closed) == 0;

    /// <inheritdoc />
    public string RemoteAddress { get; }

    /// <summary>TCP_NODELAY state of the underlying socket (always enabled here).</summary>
    public bool NoDelay => _client.NoDelay;

    public event Action<ProtocolFrame>? FrameReceived;
    public event Action<string>? Disconnected;

    /// <summary>
    /// Runs the read loop until the connection is closed (locally or remotely).
    /// Decoded frames are raised via <see cref="FrameReceived"/>. The loop
    /// completes when the peer closes cleanly, a protocol error is detected
    /// (EOF mid-frame or a frame rejected by <see cref="FrameCodec"/>), or the
    /// connection is cancelled/closed.
    /// </summary>
    public async Task RunAsync()
    {
        var buffer = new byte[8192];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var read = await _stream.ReadAsync(buffer.AsMemory(), _cts.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    if (_frameBuffer.HasBufferedData)
                        throw new ProtocolException("Connection closed mid-frame.");
                    break;
                }

                _frameBuffer.Append(buffer.AsSpan(0, read));
                while (_frameBuffer.TryReadFrame(out var rawFrame))
                    FrameReceived?.Invoke(FrameCodec.Decode(rawFrame));
            }

            CloseOnce("Remote peer closed the connection.");
        }
        catch (OperationCanceledException)
        {
            CloseOnce("Connection closed.");
        }
        catch (Exception ex)
        {
            CloseOnce($"Connection error: {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _sendGate.Dispose();
            _completed.TrySetResult();
        }
    }

    public async Task SendAsync(byte[] frame, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _closed) != 0)
            throw new InvalidOperationException("Connection is closed.");
        if (frame is null)
            throw new ArgumentNullException(nameof(frame));

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closed) != 0)
                throw new InvalidOperationException("Connection is closed.");
            await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async Task CloseAsync()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }
        CloseOnce("Connection closed.");
        try
        {
            await _completed.Task.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void CloseOnce(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        try
        {
            _client.Close();
        }
        catch
        {
        }
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }
        Disconnected?.Invoke(reason);
    }
}
