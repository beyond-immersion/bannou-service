using BeyondImmersion.Bannou.Client;
using BeyondImmersion.Bannou.Client.Voice.Services;
// Voice event models from generated code - these are included in the main SDK
// via the lib-*/Generated/*ClientEventsModels.cs pattern
using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService.Voice;
using BeyondImmersion.Bannou.Core;
using System.Collections.Concurrent;

namespace BeyondImmersion.Bannou.Client.Voice;

/// <summary>
/// High-level manager for voice chat in a Bannou voice room, supporting both P2P and scaled modes.
/// <para>
/// <b>Stride Integration Example:</b>
/// <code>
/// // In your NetworkManager or VoiceController script:
/// var voiceManager = new VoiceRoomManager(bannouClient);
/// voiceManager.OnAudioReceived += (peerSessionId, samples, rate, channels) =&gt;
/// {
///     // Play audio via Stride's DynamicSoundSource
///     PlayAudioFromPeer(peerSessionId, samples, rate, channels);
/// };
///
/// // In your game loop, send microphone audio:
/// voiceManager.SendAudioToAllPeers(microphoneSamples, 48000, 1);
/// </code>
/// </para>
/// <para>
/// <b>Automatic Tier Transition:</b><br/>
/// When 6+ participants join, the server sends a <see cref="VoiceTierUpgradeClientEvent"/>.
/// This manager automatically:
/// <list type="bullet">
///   <item>Closes all P2P connections</item>
///   <item>Connects to the SIP/RTP infrastructure using provided credentials</item>
///   <item>Redirects audio through the RTP server</item>
/// </list>
/// The <c>SendAudioToAllPeers</c> method works transparently in both modes.
/// </para>
/// <para>
/// <b>Note:</b> Video is NOT supported in multi-peer scenarios. Use <see cref="IVideoPeerConnection"/>
/// for 1:1 video calls only.
/// </para>
/// </summary>
public sealed class VoiceRoomManager : IDisposable
{
    private readonly BannouClient _client;
    private readonly Func<Guid, string?, IEnumerable<string>?, IVoicePeerConnection> _peerFactory;
    private readonly Func<Guid, IScaledVoiceConnection>? _scaledConnectionFactory;
    private readonly ConcurrentDictionary<Guid, IVoicePeerConnection> _peers = new();
    private readonly object _lock = new();
    private bool _disposed;

    private Guid? _currentRoomId;
    private VoiceTier _currentTier = VoiceTier.P2p;
    private IReadOnlyList<string> _stunServers = Array.Empty<string>();

    // Scaled tier connection (null when in P2P mode)
    private IScaledVoiceConnection? _scaledConnection;

    /// <summary>
    /// Gets the current voice room ID, or null if not in a room.
    /// </summary>
    public Guid? CurrentRoomId => _currentRoomId;

    /// <summary>
    /// Gets whether the client is currently in a voice room.
    /// </summary>
    public bool IsInRoom => _currentRoomId.HasValue;

    /// <summary>
    /// Gets the current voice tier (P2P or Scaled).
    /// </summary>
    public VoiceTier CurrentTier => _currentTier;

    /// <summary>
    /// Gets all active peer connections by peer session ID.
    /// </summary>
    public IReadOnlyDictionary<Guid, IVoicePeerConnection> Peers => _peers;

    /// <summary>
    /// Gets the scaled voice connection when in scaled mode.
    /// Null when in P2P mode.
    /// </summary>
    public IScaledVoiceConnection? ScaledConnection => _scaledConnection;

    /// <summary>
    /// Gets whether the scaled connection is active and audio is flowing.
    /// </summary>
    public bool IsScaledConnectionActive => _scaledConnection?.IsAudioActive ?? false;

    /// <summary>
    /// Gets or sets whether local audio is muted (not sent to peers or server).
    /// </summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;

            // Apply to P2P peers
            foreach (var peer in _peers.Values)
            {
                peer.IsMuted = value;
            }

            // Apply to scaled connection
            // IDE0031 suggests ?. but you can't use null propagation on assignment targets
#pragma warning disable IDE0031
            if (_scaledConnection != null)
            {
                _scaledConnection.IsMuted = value;
            }
#pragma warning restore IDE0031
        }
    }
    private bool _isMuted;

    #region Events

    /// <summary>
    /// Fired when audio is received from any peer.
    /// Parameters: (peerSessionId, samples[], sampleRate, channels)
    /// </summary>
    public event Action<Guid, float[], int, int>? OnAudioReceived;

    /// <summary>
    /// Fired when a new peer joins the room.
    /// </summary>
    public event Action<Guid, string?>? OnPeerJoined;

    /// <summary>
    /// Fired when a peer leaves the room.
    /// </summary>
    public event Action<Guid>? OnPeerLeft;

    /// <summary>
    /// Fired when the room is closed (session ended, error, etc.).
    /// </summary>
    public event Action<string>? OnRoomClosed;

    /// <summary>
    /// Fired when the voice tier upgrades from P2P to Scaled.
    /// <para>
    /// When automatic tier transition is enabled (default), the manager will
    /// handle the transition automatically. This event is informational.
    /// </para>
    /// </summary>
    public event Action<string>? OnTierUpgraded;

    /// <summary>
    /// Fired when the scaled connection state changes.
    /// </summary>
    public event Action<ScaledVoiceConnectionState>? OnScaledConnectionStateChanged;

    /// <summary>
    /// Fired when a scaled connection error occurs.
    /// Parameters: (errorCode, errorMessage)
    /// </summary>
    public event Action<ScaledVoiceErrorCode, string>? OnScaledConnectionError;

    /// <summary>
    /// Fired when a peer connection changes state.
    /// Parameters: (peerSessionId, newState)
    /// </summary>
    public event Action<Guid, VoicePeerConnectionState>? OnPeerStateChanged;

    /// <summary>
    /// Fired when an ICE candidate needs to be sent to the server.
    /// <para>
    /// The SDK cannot send this automatically because the voice service is internal-only.
    /// You must call the game session service's UpdatePeerEndpoint API to relay this.
    /// </para>
    /// Parameters: (peerSessionId, iceCandidate)
    /// </summary>
    public event Action<Guid, string>? OnIceCandidateReady;

    #endregion

    /// <summary>
    /// Creates a new VoiceRoomManager using the default SIPSorcery implementation.
    /// </summary>
    /// <param name="client">The connected BannouClient for receiving voice events.</param>
    public VoiceRoomManager(BannouClient client)
        : this(client, null, null)
    {
    }

    /// <summary>
    /// Creates a new VoiceRoomManager with custom connection factories.
    /// <para>
    /// Use this overload if you want to provide your own WebRTC or scaled tier implementation
    /// (e.g., Unity WebRTC package instead of SIPSorcery).
    /// </para>
    /// </summary>
    /// <param name="client">The connected BannouClient for receiving voice events.</param>
    /// <param name="peerFactory">Factory function to create P2P peer connections.
    /// Parameters: (peerSessionId, displayName, stunServers).
    /// If null, uses <see cref="SIPSorceryVoicePeer"/>.</param>
    /// <param name="scaledConnectionFactory">Factory function to create scaled connections.
    /// Parameters: (roomId).
    /// If null, uses <see cref="ScaledVoiceConnection"/>.</param>
    public VoiceRoomManager(
        BannouClient client,
        Func<Guid, string?, IEnumerable<string>?, IVoicePeerConnection>? peerFactory,
        Func<Guid, IScaledVoiceConnection>? scaledConnectionFactory = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _peerFactory = peerFactory ?? CreateDefaultPeer;
        _scaledConnectionFactory = scaledConnectionFactory ?? CreateDefaultScaledConnection;

        // Subscribe to voice events
        _client.OnEvent("voice.room-state", HandleRoomState);
        _client.OnEvent("voice.peer-joined", HandlePeerJoined);
        _client.OnEvent("voice.peer-left", HandlePeerLeft);
        _client.OnEvent("voice.peer-updated", HandlePeerUpdated);
        _client.OnEvent("voice.tier-upgrade", HandleTierUpgrade);
        _client.OnEvent("voice.room-closed", HandleRoomClosed);
    }

    private static IScaledVoiceConnection CreateDefaultScaledConnection(Guid roomId)
    {
        return new ScaledVoiceConnection(roomId);
    }

    private static IVoicePeerConnection CreateDefaultPeer(Guid peerSessionId, string? displayName, IEnumerable<string>? stunServers)
    {
        return new SIPSorceryVoicePeer(peerSessionId, displayName, stunServers);
    }

    /// <summary>
    /// Sends an audio frame to all connected peers (P2P mode) or the RTP server (scaled mode).
    /// <para>
    /// This method works transparently in both P2P and scaled tier modes.
    /// </para>
    /// <para>
    /// <b>Stride Integration:</b><br/>
    /// Call this from your game loop with microphone capture data.
    /// Stride's <c>Microphone</c> class provides audio samples.
    /// </para>
    /// </summary>
    /// <param name="pcmSamples">PCM audio samples as normalized floats (-1.0 to 1.0).</param>
    /// <param name="sampleRate">Sample rate in Hz (typically 48000 for Opus).</param>
    /// <param name="channels">Number of audio channels (1 for mono, 2 for stereo).</param>
    public void SendAudioToAllPeers(ReadOnlySpan<float> pcmSamples, int sampleRate, int channels = 1)
    {
        if (_disposed || IsMuted)
        {
            return;
        }

        if (_currentTier == VoiceTier.Scaled && _scaledConnection != null)
        {
            // Scaled mode: send to RTP server
            _scaledConnection.SendAudioFrame(pcmSamples, sampleRate, channels);
        }
        else if (_currentTier == VoiceTier.P2p)
        {
            // P2P mode: send to all peers
            foreach (var peer in _peers.Values)
            {
                peer.SendAudioFrame(pcmSamples, sampleRate, channels);
            }
        }
    }

    /// <summary>
    /// Manually processes an SDP answer received from a peer.
    /// <para>
    /// Call this when you receive an SDP answer via signaling (e.g., game session callback).
    /// </para>
    /// </summary>
    /// <param name="peerSessionId">The peer's session ID.</param>
    /// <param name="sdpAnswer">The SDP answer string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessSdpAnswerAsync(Guid peerSessionId, string sdpAnswer, CancellationToken cancellationToken = default)
    {
        if (_peers.TryGetValue(peerSessionId, out var peer))
        {
            await peer.SetRemoteDescriptionAsync(sdpAnswer, isOffer: false, cancellationToken);
        }
    }

    /// <summary>
    /// Manually adds an ICE candidate received from a peer.
    /// <para>
    /// Call this when you receive ICE candidates via signaling.
    /// </para>
    /// </summary>
    /// <param name="peerSessionId">The peer's session ID.</param>
    /// <param name="iceCandidate">The ICE candidate string.</param>
    public void AddIceCandidateForPeer(Guid peerSessionId, string iceCandidate)
    {
        if (_peers.TryGetValue(peerSessionId, out var peer))
        {
            peer.AddIceCandidate(iceCandidate);
        }
    }

    /// <summary>
    /// Leaves the current voice room and closes all connections (P2P and scaled).
    /// </summary>
    public async Task LeaveRoomAsync()
    {
        await CloseAllPeersAsync();
        await CloseScaledConnectionAsync();
        _currentRoomId = null;
        _currentTier = VoiceTier.P2p;
    }

    private async Task CloseScaledConnectionAsync()
    {
        if (_scaledConnection != null)
        {
            try
            {
                await _scaledConnection.DisconnectAsync();
            }
            catch
            {
                // Ignore disconnect errors
            }
            _scaledConnection.Dispose();
            _scaledConnection = null;
        }
    }

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

                    // Unsubscribe from events
                    _client.RemoveEventHandler("voice.room-state");
                    _client.RemoveEventHandler("voice.peer-joined");
                    _client.RemoveEventHandler("voice.peer-left");
                    _client.RemoveEventHandler("voice.peer-updated");
                    _client.RemoveEventHandler("voice.tier-upgrade");
                    _client.RemoveEventHandler("voice.room-closed");

                    // Close all P2P peers
                    foreach (var peer in _peers.Values)
                    {
                        peer.Dispose();
                    }
                    _peers.Clear();

                    // Close scaled connection
                    _scaledConnection?.Dispose();
                    _scaledConnection = null;
                }
            }
        }
    }

    #region Event Handlers

    private void HandleRoomState(string json)
    {
        try
        {
            var evt = BannouJson.Deserialize<VoiceRoomStateClientEvent>(json);
            if (evt == null) return;

            _currentRoomId = evt.RoomId;
            _currentTier = evt.Tier;
            _stunServers = evt.StunServers?.ToList() ?? new List<string>();

            // If P2P mode, connect to all existing peers
            if (evt.Tier == VoiceTier.P2p)
            {
                foreach (var peerInfo in evt.Peers)
                {
                    _ = ConnectToPeerAsync(peerInfo);
                }
            }
        }
        catch
        {
            // Ignore parse errors
        }
    }

    private void HandlePeerJoined(string json)
    {
        try
        {
            var evt = BannouJson.Deserialize<VoicePeerJoinedClientEvent>(json);
            if (evt == null || evt.RoomId != _currentRoomId) return;

            if (_currentTier == VoiceTier.P2p)
            {
                _ = ConnectToPeerAsync(evt.Peer);
            }

            OnPeerJoined?.Invoke(evt.Peer.PeerSessionId, evt.Peer.DisplayName);
        }
        catch
        {
            // Ignore parse errors
        }
    }

    private void HandlePeerLeft(string json)
    {
        try
        {
            var evt = BannouJson.Deserialize<VoicePeerLeftClientEvent>(json);
            if (evt == null || evt.RoomId != _currentRoomId) return;

            if (_peers.TryRemove(evt.PeerSessionId, out var peer))
            {
                peer.Dispose();
            }

            OnPeerLeft?.Invoke(evt.PeerSessionId);
        }
        catch
        {
            // Ignore parse errors
        }
    }

    private void HandlePeerUpdated(string json)
    {
        try
        {
            var evt = BannouJson.Deserialize<VoicePeerUpdatedClientEvent>(json);
            if (evt == null || evt.RoomId != _currentRoomId) return;

            // Add any new ICE candidates from the peer
            if (evt.Peer.IceCandidates != null && _peers.TryGetValue(evt.Peer.PeerSessionId, out var peer))
            {
                foreach (var candidate in evt.Peer.IceCandidates)
                {
                    peer.AddIceCandidate(candidate);
                }
            }
        }
        catch
        {
            // Ignore parse errors
        }
    }

    private void HandleTierUpgrade(string json)
    {
        try
        {
            var evt = BannouJson.Deserialize<VoiceTierUpgradeClientEvent>(json);
            if (evt == null || evt.RoomId != _currentRoomId) return;

            _currentTier = VoiceTier.Scaled;

            // Close all P2P connections - audio now goes through RTP server
            _ = CloseAllPeersAsync();

            // Fire event before attempting connection
            OnTierUpgraded?.Invoke(evt.RtpServerUri);

            // Attempt automatic tier transition if we have credentials
            if (evt.SipCredentials != null && _scaledConnectionFactory != null)
            {
                _ = TransitionToScaledTierAsync(evt);
            }
        }
        catch
        {
            // Ignore parse errors
        }
    }

    private async Task TransitionToScaledTierAsync(VoiceTierUpgradeClientEvent evt)
    {
        if (!_currentRoomId.HasValue || evt.SipCredentials == null)
        {
            return;
        }

        try
        {
            // Create scaled connection
            _scaledConnection = _scaledConnectionFactory!(_currentRoomId.Value);

            // Wire up events
            _scaledConnection.OnStateChanged += (state) =>
            {
                OnScaledConnectionStateChanged?.Invoke(state);
            };

            _scaledConnection.OnError += (errorCode, message) =>
            {
                OnScaledConnectionError?.Invoke(errorCode, message);
            };

            _scaledConnection.OnAudioFrameReceived += (samples, rate, channels) =>
            {
                // In scaled mode, audio from server doesn't have a specific peer session ID
                // Use Guid.Empty to represent the mixed server stream
                OnAudioReceived?.Invoke(Guid.Empty, samples, rate, channels);
            };

            _scaledConnection.OnDisconnected += (reason) =>
            {
                // Handle unexpected disconnection
                if (_currentTier == VoiceTier.Scaled)
                {
                    OnScaledConnectionError?.Invoke(ScaledVoiceErrorCode.ServerDisconnect, reason ?? "Connection lost");
                }
            };

            // Apply mute state
            _scaledConnection.IsMuted = IsMuted;

            // Construct conference URI from domain and room ID
            var conferenceUri = $"sip:room-{evt.RoomId}@{evt.SipCredentials.Domain}";

            // Build SIP credentials from event
            var sipCredentials = SipConnectionCredentials.FromEvent(
                evt.SipCredentials,
                conferenceUri);

            // Connect to scaled infrastructure
            var connected = await _scaledConnection.ConnectAsync(
                sipCredentials,
                evt.RtpServerUri);

            if (!connected)
            {
                _scaledConnection.Dispose();
                _scaledConnection = null;
                OnScaledConnectionError?.Invoke(ScaledVoiceErrorCode.Unknown, "Failed to connect to scaled voice infrastructure");
            }
        }
        catch (Exception ex)
        {
            _scaledConnection?.Dispose();
            _scaledConnection = null;
            OnScaledConnectionError?.Invoke(ScaledVoiceErrorCode.Unknown, $"Error transitioning to scaled tier: {ex.Message}");
        }
    }

    private void HandleRoomClosed(string json)
    {
        try
        {
            var evt = BannouJson.Deserialize<VoiceRoomClosedClientEvent>(json);
            if (evt == null || evt.RoomId != _currentRoomId) return;

            _ = CloseAllPeersAsync();
            _ = CloseScaledConnectionAsync();
            _currentRoomId = null;
            _currentTier = VoiceTier.P2p;

            OnRoomClosed?.Invoke(evt.Reason.ToString());
        }
        catch
        {
            // Ignore parse errors
        }
    }

    #endregion

    #region Private Methods

    private async Task ConnectToPeerAsync(VoicePeerInfo peerInfo)
    {
        var peerSessionId = peerInfo.PeerSessionId;

        if (_peers.ContainsKey(peerSessionId))
        {
            return; // Already connected
        }

        var peer = _peerFactory(peerSessionId, peerInfo.DisplayName, _stunServers);
        _peers[peerSessionId] = peer;

        // Wire up events
        peer.OnAudioFrameReceived += (samples, sampleRate, channels) =>
        {
            OnAudioReceived?.Invoke(peerSessionId, samples, sampleRate, channels);
        };

        peer.OnStateChanged += (state) =>
        {
            OnPeerStateChanged?.Invoke(peerSessionId, state);
        };

        peer.OnIceCandidateGathered += (candidate) =>
        {
            OnIceCandidateReady?.Invoke(peerSessionId, candidate);
        };

        peer.IsMuted = IsMuted;

        try
        {
            // Set remote SDP offer from the peer
            await peer.SetRemoteDescriptionAsync(peerInfo.SdpOffer, isOffer: true);

            // Add any ICE candidates they've already gathered
            if (peerInfo.IceCandidates != null)
            {
                foreach (var candidate in peerInfo.IceCandidates)
                {
                    peer.AddIceCandidate(candidate);
                }
            }

            // Create our answer
            var answer = await peer.CreateAnswerAsync();

            // The caller needs to send this answer back to the server
            // This is done via the OnIceCandidateReady event or a separate callback
            // For now, we just log that the answer is ready
            // TODO: Consider adding an OnSdpAnswerReady event
        }
        catch
        {
            // Connection failed - remove peer
            _peers.TryRemove(peerSessionId, out _);
            peer.Dispose();
        }
    }

    private async Task CloseAllPeersAsync()
    {
        var peers = _peers.Values.ToList();
        _peers.Clear();

        foreach (var peer in peers)
        {
            try
            {
                await peer.CloseAsync();
            }
            catch
            {
                // Ignore close errors
            }
            peer.Dispose();
        }
    }

    #endregion
}
