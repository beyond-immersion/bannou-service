using BeyondImmersion.Bannou.Client.Voice.Services;

namespace BeyondImmersion.Bannou.Client.Voice;

/// <summary>
/// Implementation of scaled voice connection using SIP/RTP (Kamailio + RTPEngine).
/// <para>
/// <b>Usage:</b><br/>
/// <code>
/// // When VoiceTierUpgradeEvent is received:
/// var connection = new ScaledVoiceConnection(roomId);
/// connection.OnAudioFrameReceived += (samples, rate, channels) => PlayAudio(samples, rate, channels);
/// connection.OnConnected += () => Console.WriteLine("Connected to voice conference!");
///
/// var credentials = SipConnectionCredentials.FromEvent(event.SipCredentials, event.ConferenceUri);
/// await connection.ConnectAsync(credentials, event.RtpServerUri);
///
/// // Send audio from microphone:
/// connection.SendAudioFrame(microphonePcm, 48000, 1);
///
/// // When leaving the room:
/// await connection.DisconnectAsync();
/// </code>
/// </para>
/// </summary>
public class ScaledVoiceConnection : IScaledVoiceConnection
{
    private readonly object _lock = new();
    private readonly SipRegistrationHelper _sipHelper;
    private readonly RtpStreamHelper _rtpHelper;
    private ScaledVoiceConnectionState _state = ScaledVoiceConnectionState.Disconnected;
    private SipConnectionCredentials? _currentCredentials;
    private string? _rtpServerUri;
    private bool _disposed;

    #region IScaledVoiceConnection Properties

    /// <inheritdoc/>
    public Guid RoomId { get; }

    /// <inheritdoc/>
    public ScaledVoiceConnectionState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                OnStateChanged?.Invoke(value);
            }
        }
    }

    /// <inheritdoc/>
    public bool IsAudioActive => _rtpHelper.IsActive && _sipHelper.IsRegistered;

    /// <inheritdoc/>
    /// <remarks>Returns empty string when not yet connected (no credentials set).</remarks>
    public string SipUsername => _sipHelper.Username ?? string.Empty;

    /// <inheritdoc/>
    public bool IsMuted
    {
        get => _rtpHelper.IsMuted;
        set => _rtpHelper.IsMuted = value;
    }

    #endregion

    #region IScaledVoiceConnection Events

    /// <inheritdoc/>
    public event Action<ScaledVoiceConnectionState>? OnStateChanged;

    /// <inheritdoc/>
    public event Action<ScaledVoiceErrorCode, string>? OnError;

    /// <inheritdoc/>
    public event Action? OnConnected;

    /// <inheritdoc/>
    public event Action<string?>? OnDisconnected;

    /// <inheritdoc/>
    public event Action<int>? OnRegistrationExpiring;

    /// <inheritdoc/>
    public event Action<float[], int, int>? OnAudioFrameReceived;

    #endregion

    /// <summary>
    /// Creates a new scaled voice connection for the specified room.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    public ScaledVoiceConnection(Guid roomId)
    {
        RoomId = roomId;
        _sipHelper = new SipRegistrationHelper();
        _rtpHelper = new RtpStreamHelper();

        // Wire up internal events
        WireUpInternalEvents();
    }

    /// <summary>
    /// Creates a new scaled voice connection with custom helpers (for testing).
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="sipHelper">Custom SIP registration helper.</param>
    /// <param name="rtpHelper">Custom RTP stream helper.</param>
    internal ScaledVoiceConnection(
        Guid roomId,
        SipRegistrationHelper sipHelper,
        RtpStreamHelper rtpHelper)
    {
        RoomId = roomId;
        _sipHelper = sipHelper;
        _rtpHelper = rtpHelper;

        WireUpInternalEvents();
    }

    #region IScaledVoiceConnection Methods

    /// <inheritdoc/>
    public async Task<bool> ConnectAsync(
        SipConnectionCredentials sipCredentials,
        string rtpServerUri,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (State == ScaledVoiceConnectionState.Connected ||
            State == ScaledVoiceConnectionState.Registering ||
            State == ScaledVoiceConnectionState.Dialing)
        {
            throw new InvalidOperationException($"Cannot connect: current state is {State}");
        }

        _currentCredentials = sipCredentials;
        _rtpServerUri = rtpServerUri;

        try
        {
            // Phase 1: Register with SIP server
            State = ScaledVoiceConnectionState.Registering;

            var sipRegistered = await _sipHelper.RegisterAsync(sipCredentials, cancellationToken: cancellationToken);
            if (!sipRegistered)
            {
                State = ScaledVoiceConnectionState.Failed;
                OnError?.Invoke(ScaledVoiceErrorCode.RegistrationFailed, "SIP registration failed");
                return false;
            }

            // Phase 2: Start RTP stream
            State = ScaledVoiceConnectionState.Dialing;

            // Determine codec from credentials or default to Opus
            var codec = "opus"; // Could be configurable

            var rtpStarted = await _rtpHelper.StartAsync(rtpServerUri, codec, cancellationToken: cancellationToken);
            if (!rtpStarted)
            {
                await _sipHelper.UnregisterAsync();
                State = ScaledVoiceConnectionState.Failed;
                OnError?.Invoke(ScaledVoiceErrorCode.RtpSetupFailed, "RTP stream setup failed");
                return false;
            }

            // Success!
            State = ScaledVoiceConnectionState.Connected;
            OnConnected?.Invoke();
            return true;
        }
        catch (OperationCanceledException)
        {
            State = ScaledVoiceConnectionState.Disconnected;
            throw;
        }
        catch (Exception ex)
        {
            State = ScaledVoiceConnectionState.Failed;
            OnError?.Invoke(ScaledVoiceErrorCode.Unknown, ex.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();

        if (State == ScaledVoiceConnectionState.Disconnected)
        {
            return;
        }

        try
        {
            // Stop RTP first (graceful audio shutdown)
            await _rtpHelper.StopAsync();

            // Then de-register from SIP
            await _sipHelper.UnregisterAsync();

            State = ScaledVoiceConnectionState.Disconnected;
            _currentCredentials = null;
            _rtpServerUri = null;
            OnDisconnected?.Invoke("User requested disconnect");
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ScaledVoiceErrorCode.Unknown, $"Error during disconnect: {ex.Message}");
            State = ScaledVoiceConnectionState.Disconnected;
        }
    }

    /// <inheritdoc/>
    public async Task RefreshRegistrationAsync(
        SipConnectionCredentials? newCredentials = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (State != ScaledVoiceConnectionState.Connected)
        {
            throw new InvalidOperationException($"Cannot refresh: current state is {State}");
        }

        try
        {
            if (newCredentials != null)
            {
                _currentCredentials = newCredentials;
            }

            await _sipHelper.RefreshAsync(newCredentials, cancellationToken);
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ScaledVoiceErrorCode.CredentialsExpired, $"Failed to refresh registration: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public void SendAudioFrame(ReadOnlySpan<float> pcmSamples, int sampleRate, int channels = 1)
    {
        if (_disposed || State != ScaledVoiceConnectionState.Connected)
        {
            return;
        }

        _rtpHelper.SendAudioFrame(pcmSamples, sampleRate, channels);
    }

    /// <inheritdoc/>
    public RtpStreamStatistics GetStatistics()
    {
        ThrowIfDisposed();
        return _rtpHelper.GetStatistics();
    }

    #endregion

    #region Reconnection Support

    /// <summary>
    /// Attempts to reconnect using the last known credentials.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if reconnection succeeded.</returns>
    public async Task<bool> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_currentCredentials == null || _rtpServerUri == null)
        {
            throw new InvalidOperationException("Cannot reconnect: no previous connection credentials");
        }

        State = ScaledVoiceConnectionState.Reconnecting;

        // Stop existing connections
        await _rtpHelper.StopAsync();
        await _sipHelper.UnregisterAsync();

        // Try to reconnect
        return await ConnectAsync(_currentCredentials, _rtpServerUri, cancellationToken);
    }

    #endregion

    #region Private Methods

    private void WireUpInternalEvents()
    {
        // Forward SIP events
        _sipHelper.OnRegistered += () =>
        {
            // SIP registered, waiting for RTP
        };

        _sipHelper.OnRegistrationFailed += (statusCode, message) =>
        {
            var errorCode = statusCode switch
            {
                401 or 403 => ScaledVoiceErrorCode.RegistrationFailed,
                408 => ScaledVoiceErrorCode.RegistrationTimeout,
                _ => ScaledVoiceErrorCode.RegistrationFailed
            };
            OnError?.Invoke(errorCode, $"SIP registration failed ({statusCode}): {message}");
        };

        _sipHelper.OnRegistrationExpiring += (seconds) =>
        {
            OnRegistrationExpiring?.Invoke(seconds);
        };

        _sipHelper.OnUnregistered += () =>
        {
            if (State == ScaledVoiceConnectionState.Connected)
            {
                // Unexpected unregistration
                State = ScaledVoiceConnectionState.Disconnected;
                OnDisconnected?.Invoke("SIP registration lost");
            }
        };

        // Forward RTP events
        _rtpHelper.OnAudioFrameReceived += (samples, rate, channels) =>
        {
            OnAudioFrameReceived?.Invoke(samples, rate, channels);
        };

        _rtpHelper.OnError += (message) =>
        {
            OnError?.Invoke(ScaledVoiceErrorCode.NetworkError, message);
        };

        _rtpHelper.OnSsrcChanged += (ssrc) =>
        {
            // SSRC changed - this is normal when participants join/leave
            // No action needed, just informational
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _disposed = true;

                    _rtpHelper.Dispose();
                    _sipHelper.Dispose();

                    State = ScaledVoiceConnectionState.Disconnected;
                }
            }
        }
        GC.SuppressFinalize(this);
    }

    #endregion
}
