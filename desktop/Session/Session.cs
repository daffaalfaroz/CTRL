using CTRL.Desktop.Protocol;
using CTRL.Desktop.Transport;

namespace CTRL.Desktop.Session;

public sealed class SessionOptions
{
    public string ServerName { get; init; } = "CTRL-PC";
    public byte ProtocolMajor { get; init; } = 1;
    public byte ProtocolMinor { get; init; } = 0;
    public byte MinSupportedMajor { get; init; } = 1;
    public uint ServerCapabilities { get; init; } = 0x00000007;
    public bool AuthRequired { get; init; } = true;
    public ulong AckTimeoutMs { get; init; } = 3000;

    /// <summary>Server-side heartbeat timeout (docs/protocol.md §10): the
    /// connection is considered dead when READY and no frame arrives for this
    /// long. The client heartbeats every 1000 ms, so 3000 ms ≈ 3 misses.</summary>
    public ulong HeartbeatTimeoutMs { get; init; } = 3000;

    public Func<ulong> NowMs { get; init; } = () => (ulong)Environment.TickCount64;
    public Func<byte[]>? SessionIdFactory { get; init; }
    public Func<byte[]>? ChallengeFactory { get; init; }
}

/// <summary>
/// Server-side CTRL session: drives the state machine (docs/protocol.md §19),
/// negotiates protocol version, authenticates via <see cref="IAuthenticator"/>,
/// validates control-plane payloads, and forwards input-plane records to the
/// listener. Malformed input-plane payloads are dropped (§18, §21.7); malformed
/// control-plane payloads answer ERROR invalid-message and close (D5).
/// </summary>
public sealed class Session
{
    private readonly ITransportConnection _connection;
    private readonly IAuthenticator _authenticator;
    private readonly ISessionListener _listener;
    private readonly IInputStateFlusher? _flusher;
    private readonly SessionOptions _options;
    private SequenceTracker _outbound = new();
    private InboundSequenceTracker _inbound = new();
    private readonly AckTracker _ackTracker;
    private readonly Dictionary<ushort, (byte Type, byte[] Payload, bool MustUnderstand)> _pendingSends = new();

    private ServerSessionState _state;
    private readonly byte[] _sessionId;
    private readonly byte[] _challenge;
    private byte _effectiveMajor;
    private string _deviceId = "";
    private int _closedFlag;
    private ulong _lastActivityMs;

    /// <summary>Raised when the session reaches Ready (authenticated).</summary>
    public event Action<Session>? Authenticated;

    /// <summary>Raised exactly once when the session terminates.</summary>
    public event Action<Session>? Closed;

    public Session(
        ITransportConnection connection,
        IAuthenticator authenticator,
        ISessionListener listener,
        SessionOptions options,
        IInputStateFlusher? flusher = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _flusher = flusher;
        _ackTracker = new AckTracker(options.NowMs, options.AckTimeoutMs);
        _effectiveMajor = options.ProtocolMajor;
        _sessionId = (options.SessionIdFactory ?? NewSessionId)();
        _challenge = (options.ChallengeFactory ?? NewChallenge)();
        _lastActivityMs = options.NowMs();

        SetState(ServerSessionState.WaitHello);
        _connection.FrameReceived += OnFrameReceived;
        _connection.Disconnected += OnDisconnected;
    }

    public ServerSessionState State => _state;
    public string DeviceId => _deviceId;
    public byte[] SessionId => _sessionId;

    /// <summary>Number of outbound messages still awaiting an ACK. Test hook
    /// proving e.g. PONG/HEARTBEAT never enter the ACK tracker (§10/§11).</summary>
    public int PendingAckCount => _ackTracker.PendingCount;

    /// <summary>
    /// Simulates a keepalive/ACK timer tick: retries each pending message once
    /// after <see cref="SessionOptions.AckTimeoutMs"/>, then fails the session
    /// when the retry also times out. Tests drive this directly.
    /// </summary>
    public void ProcessPendingAcks()
    {
        foreach (var sequence in _ackTracker.RetryExpired())
            Retransmit(sequence);

        foreach (var sequence in _ackTracker.Failed())
        {
            _pendingSends.Remove(sequence);
            _listener.OnError($"Message sequence {sequence} unacknowledged after retry; session considered unhealthy.");
            Close(ServerSessionState.Closing, "Unacknowledged message after retry.");
        }
    }

    /// <summary>
    /// D3: another connection authenticated with the same deviceId. Notify this
    /// (old) session with ERROR device-limit, then close + flush pending inputs.
    /// </summary>
    public void TerminateForTakeover()
    {
        if (_state is ServerSessionState.Closing or ServerSessionState.Closed)
            return;

        Send(MessageType.Error,
            ErrorPayloadCodec.Encode(new ErrorPayload(
                ErrorPayloadCodec.CodeDeviceLimit,
                ErrorPayloadCodec.SeverityWarn,
                "Session taken over by a new connection for this device.")),
            mustUnderstand: true);
        Close(ServerSessionState.Closing, "Session taken over by a new connection.");
    }

    /// <summary>
    /// Marks the session as active at the given time. Called automatically on
    /// every received frame; exposed so the host loop can drive it on a fake
    /// clock in deterministic tests.
    /// </summary>
    public void Touch(ulong nowMs) => _lastActivityMs = nowMs;

    /// <summary>
    /// Heartbeat liveness check (docs/protocol.md §10): when READY and no frame
    /// has arrived within <see cref="SessionOptions.HeartbeatTimeoutMs"/>, the
    /// connection is marked dead — close + flush. Runs on the injected clock so
    /// tests drive it directly instead of sleeping.
    /// </summary>
    public bool CheckHeartbeatTimeout()
    {
        if (_state != ServerSessionState.Ready)
            return false;
        if (_options.NowMs() - _lastActivityMs < _options.HeartbeatTimeoutMs)
            return false;
        Close(ServerSessionState.Closing, "Heartbeat timeout.");
        return true;
    }

    /// <summary>
    /// Sends a control-plane message that requests an ACK (docs/protocol.md §11,
    /// e.g. CONFIG_PUSH). The ACK is tracked with retry-once semantics; used by
    /// the ACK lifecycle tests and the cross-language integration harness.
    /// </summary>
    public void SendWithAck(byte messageType, byte[] payload) =>
        Send(messageType, payload, mustUnderstand: true, ackRequested: true);

    private void OnFrameReceived(ProtocolFrame frame)
    {
        _lastActivityMs = _options.NowMs();
        try
        {
            ProcessFrame(frame);
        }
        catch (Exception ex)
        {
            _listener.OnError($"Unhandled frame processing error: {ex.Message}");
            Close(ServerSessionState.Closing, "Internal frame processing error.");
        }
    }

    private void ProcessFrame(ProtocolFrame frame)
    {
        if (_state is ServerSessionState.Closing or ServerSessionState.Closed)
            return;

        if ((frame.Flags & FrameCodec.ReservedFlagsMask) != 0)
        {
            SendErrorAndClose(ErrorPayloadCodec.CodeForbidden,
                "Reserved frame flags are forbidden in protocol v1.");
            return;
        }

        if (frame.VersionMajor != _effectiveMajor)
        {
            if (frame.MessageType is MessageType.Hello or MessageType.Welcome)
            {
                SendErrorAndClose(ErrorPayloadCodec.CodeProtocolVersionMismatch,
                    $"Unsupported protocol version {frame.VersionMajor}.{frame.VersionMinor}.");
            }
            else
            {
                Close(ServerSessionState.Closing, "Unsupported protocol version in frame.");
            }
            return;
        }

        if (!_inbound.IsMonotonic(frame.Sequence))
            _listener.OnError($"Non-monotonic inbound sequence {frame.Sequence} (recorded; not fatal).");

        if ((frame.Flags & FrameCodec.AckRequested) != 0)
            SendAck(frame.Sequence);

        switch (frame.MessageType)
        {
            case MessageType.Hello:
                HandleHello(frame);
                break;
            case MessageType.Auth:
                HandleAuth(frame);
                break;
            case MessageType.InputEvent:
                HandleInputEvent(frame);
                break;
            case MessageType.InputSnapshot:
                HandleInputSnapshot(frame);
                break;
            case MessageType.Heartbeat:
                HandleHeartbeat(frame);
                break;
            case MessageType.Ack:
                HandleAck(frame);
                break;
            case MessageType.Error:
                HandleError(frame);
                break;
            case MessageType.Disconnect:
                HandleDisconnect(frame);
                break;
            default:
                HandleOther(frame);
                break;
        }
    }

    private void HandleHello(ProtocolFrame frame)
    {
        if (_state != ServerSessionState.WaitHello)
        {
            SendErrorAndClose(ErrorPayloadCodec.CodeInvalidMessage, "HELLO received out of state.");
            return;
        }

        if (!TryDecodeControl(frame.MessageType, frame.Payload, HelloPayloadCodec.Decode, out HelloPayload hello))
            return;

        if (hello.ProtocolMajor > _options.ProtocolMajor ||
            hello.ProtocolMajor < _options.MinSupportedMajor)
        {
            SendErrorAndClose(ErrorPayloadCodec.CodeProtocolVersionMismatch,
                $"Unsupported protocol version {hello.ProtocolMajor}.{hello.ProtocolMinor}.");
            return;
        }

        _deviceId = hello.DeviceId;
        _effectiveMajor = hello.ProtocolMajor;

        var welcome = new WelcomePayload(
            _options.ServerName,
            hello.ProtocolMajor,
            Math.Min(hello.ProtocolMinor, _options.ProtocolMinor),
            _options.MinSupportedMajor,
            _sessionId,
            _options.AuthRequired,
            _challenge);
        Send(MessageType.Welcome, WelcomePayloadCodec.Encode(welcome), mustUnderstand: true);
        SetState(ServerSessionState.WaitAuth);
    }

    private void HandleAuth(ProtocolFrame frame)
    {
        if (_state != ServerSessionState.WaitAuth)
        {
            SendErrorAndClose(ErrorPayloadCodec.CodeInvalidMessage, "AUTH received out of state.");
            return;
        }

        if (!TryDecodeControl(frame.MessageType, frame.Payload, AuthPayloadCodec.Decode, out AuthPayload auth))
            return;

        if (auth.DeviceId != _deviceId)
        {
            SendErrorAndClose(ErrorPayloadCodec.CodeInvalidMessage,
                "AUTH deviceId does not match HELLO deviceId.");
            return;
        }

        var result = _authenticator.Authenticate(auth, _challenge, _connection.RemoteAddress);
        if (!result.Accepted)
        {
            Send(MessageType.AuthDenied,
                AuthDeniedPayloadCodec.Encode(new AuthDeniedPayload(result.DeniedReason, result.DeniedMessage)),
                mustUnderstand: true);
            Close(ServerSessionState.Closing, "Authentication failed.");
            return;
        }

        var authOk = new AuthOkPayload(
            AuthOkPayloadCodec.ResultOk,
            _sessionId,
            _options.ServerCapabilities,
            result.NewToken ?? Array.Empty<byte>());
        Send(MessageType.AuthOk, AuthOkPayloadCodec.Encode(authOk), mustUnderstand: true);
        // docs/protocol.md §7/§24.5: the per-direction sequence counter starts
        // from 0 exactly after AUTH_OK — reset both directions at this boundary.
        _outbound = new SequenceTracker();
        _inbound = new InboundSequenceTracker();
        SetState(ServerSessionState.Ready);
        Authenticated?.Invoke(this);
    }

    private void HandleInputEvent(ProtocolFrame frame)
    {
        if (_state != ServerSessionState.Ready)
        {
            SendErrorAndClose(ErrorPayloadCodec.CodeNotAuthenticated,
                "Application-plane message received before authentication.");
            return;
        }

        InputEvent inputEvent;
        try
        {
            inputEvent = InputEventCodec.Decode(frame.Payload);
        }
        catch (ProtocolException ex)
        {
            _listener.OnError($"Dropped malformed INPUT_EVENT payload: {ex.Message}");
            return;
        }
        _listener.OnInputEvent(inputEvent);
    }

    private void HandleInputSnapshot(ProtocolFrame frame)
    {
        if (_state != ServerSessionState.Ready)
        {
            SendErrorAndClose(ErrorPayloadCodec.CodeNotAuthenticated,
                "Application-plane message received before authentication.");
            return;
        }

        InputSnapshotPayload snapshot;
        try
        {
            snapshot = InputSnapshotPayloadCodec.Decode(frame.Payload);
        }
        catch (ProtocolException ex)
        {
            _listener.OnError($"Dropped malformed INPUT_SNAPSHOT payload: {ex.Message}");
            return;
        }
        _listener.OnInputSnapshot(snapshot);
    }

    private void HandleHeartbeat(ProtocolFrame frame)
    {
        if (_state != ServerSessionState.Ready)
        {
            SendErrorAndClose(ErrorPayloadCodec.CodeInvalidMessage, "HEARTBEAT received out of state.");
            return;
        }

        if (!TryDecodeControl(frame.MessageType, frame.Payload, HeartbeatPayloadCodec.Decode, out HeartbeatPayload heartbeat))
            return;

        // D7: recognition only — reply PONG, no liveness timers in this milestone.
        Send(MessageType.Pong, PongPayloadCodec.Encode(new PongPayload(heartbeat.ClientSendTime, _options.NowMs())));
    }

    private void HandleAck(ProtocolFrame frame)
    {
        if (!TryDecodeControl(frame.MessageType, frame.Payload, AckPayloadCodec.Decode, out AckPayload ack))
            return;

        _ackTracker.Acknowledge(ack.AckedSequence);
        _pendingSends.Remove(ack.AckedSequence);
    }

    private void HandleError(ProtocolFrame frame)
    {
        if (!TryDecodeControl(frame.MessageType, frame.Payload, ErrorPayloadCodec.Decode, out ErrorPayload error))
            return;

        _listener.OnError($"Peer sent ERROR 0x{error.Code:X2} ({error.Message}).");
        if (error.Severity == ErrorPayloadCodec.SeverityFatal)
            Close(ServerSessionState.Closing, "Peer reported a fatal error.");
    }

    private void HandleDisconnect(ProtocolFrame frame)
    {
        if (!TryDecodeControl(frame.MessageType, frame.Payload, DisconnectPayloadCodec.Decode, out DisconnectPayload disconnect))
            return;

        _listener.OnError($"Peer sent DISCONNECT (reason 0x{disconnect.Reason:X2}).");
        Close(ServerSessionState.Closing, "Peer requested disconnect.");
    }

    private void HandleOther(ProtocolFrame frame)
    {
        var type = frame.MessageType;

        // Known server→client types that a client must never send to the server.
        if (type is MessageType.Welcome or MessageType.AuthOk or MessageType.AuthDenied
            or MessageType.InputReset or MessageType.Pong
            or MessageType.ProfileList or MessageType.ProfileSelected
            or MessageType.Status or MessageType.GamepadStatus)
        {
            SendErrorAndClose(ErrorPayloadCodec.CodeInvalidMessage,
                $"Message type 0x{type:X2} is not valid from client to server.");
            return;
        }

        if ((frame.Flags & FrameCodec.MustUnderstand) != 0)
        {
            SendErrorAndClose(ErrorPayloadCodec.CodeUnsupportedMessage,
                $"Unsupported message type 0x{type:X2}.");
        }
        else
        {
            _listener.OnError($"Ignored unsupported message type 0x{type:X2} (MUST_UNDERSTAND not set).");
        }
    }

    private bool TryDecodeControl<T>(
        byte type, byte[] payload, Func<byte[], T> decode, out T result)
    {
        try
        {
            result = decode(payload);
            return true;
        }
        catch (ProtocolException ex)
        {
            SendErrorAndClose(ErrorPayloadCodec.CodeInvalidMessage,
                $"Malformed control-plane payload (type 0x{type:X2}): {ex.Message}");
            result = default!;
            return false;
        }
    }

    private void Send(byte type, byte[] payload, bool mustUnderstand = false, bool ackRequested = false)
    {
        if (_state == ServerSessionState.Closed)
            return;
        var sequence = _outbound.Next();
        SendWithSequence(sequence, type, payload, mustUnderstand, ackRequested);
    }

    private void SendWithSequence(
        ushort sequence, byte type, byte[] payload, bool mustUnderstand,
        bool ackRequested = false, bool trackAck = true)
    {
        var bytes = FrameBuilder.Build(
            type, payload, sequence, ackRequested, mustUnderstand,
            _effectiveMajor, _options.ProtocolMinor, _options.NowMs());

        if (ackRequested && trackAck)
        {
            _ackTracker.Track(sequence);
            _pendingSends[sequence] = (type, payload, mustUnderstand);
        }

        _ = SendSafeAsync(bytes);
    }

    private void Retransmit(ushort sequence)
    {
        if (!_pendingSends.TryGetValue(sequence, out var pending))
            return;
        _listener.OnError($"Retransmitting sequence {sequence} after ACK timeout.");
        // Re-send the same sequence with ACK_REQUESTED but do NOT re-track:
        // the pending entry keeps its attempt count so the retry fires once and
        // the next deadline triggers failure (docs/protocol.md §7).
        SendWithSequence(sequence, pending.Type, pending.Payload, pending.MustUnderstand,
            ackRequested: true, trackAck: false);
        _ackTracker.Reschedule(sequence);
    }

    private async Task SendSafeAsync(byte[] bytes)
    {
        try
        {
            await _connection.SendAsync(bytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _listener.OnError($"Send failed: {ex.Message}");
        }
    }

    private void SendAck(ushort ackedSequence)
    {
        Send(MessageType.Ack, AckPayloadCodec.Encode(new AckPayload(ackedSequence, _options.NowMs())));
    }

    private void SendErrorAndClose(byte code, string message)
    {
        Send(MessageType.Error,
            ErrorPayloadCodec.Encode(new ErrorPayload(code, ErrorPayloadCodec.SeverityFatal, message)),
            mustUnderstand: true);
        Close(ServerSessionState.Closing, $"Sent ERROR 0x{code:X2}; closing connection.");
    }

    private void Close(ServerSessionState terminal, string reason)
    {
        if (Interlocked.Exchange(ref _closedFlag, 1) != 0)
            return;

        SetState(terminal);
        _listener.OnError(reason);
        _flusher?.Flush();
        _ = CloseSafeAsync();
    }

    private async Task CloseSafeAsync()
    {
        try
        {
            await _connection.CloseAsync().ConfigureAwait(false);
        }
        catch
        {
            // Transport teardown is best-effort; state finalization below still runs.
        }

        if (Volatile.Read(ref _closedFlag) == 0)
            Interlocked.Exchange(ref _closedFlag, 1);
        SetState(ServerSessionState.Closed);
        Closed?.Invoke(this);
    }

    private void OnDisconnected(string reason)
    {
        if (Interlocked.Exchange(ref _closedFlag, 1) != 0)
            return;

        SetState(ServerSessionState.Closing);
        _listener.OnError($"Connection lost: {reason}");
        _flusher?.Flush();
        SetState(ServerSessionState.Closed);
        Closed?.Invoke(this);
    }

    private void SetState(ServerSessionState state)
    {
        _state = state;
        _listener.OnStateChanged(state);
    }

    private static byte[] NewSessionId()
    {
        var bytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    private static byte[] NewChallenge()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
}