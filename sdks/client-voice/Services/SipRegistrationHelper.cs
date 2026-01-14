using BeyondImmersion.Bannou.Voice.ClientEvents;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using System.Net;

namespace BeyondImmersion.Bannou.Client.Voice.Services;

/// <summary>
/// Helper class for managing SIP registration with Kamailio in scaled voice tier.
/// <para>
/// <b>Purpose:</b><br/>
/// Handles the SIP REGISTER flow with Kamailio server using credentials
/// provided by the <see cref="VoiceTierUpgradeEvent"/>. Manages registration
/// lifecycle including initial registration, re-registration, and de-registration.
/// </para>
/// <para>
/// <b>Usage Pattern:</b><br/>
/// <code>
/// var helper = new SipRegistrationHelper();
/// helper.OnRegistered += () => Console.WriteLine("Registered!");
/// helper.OnRegistrationExpiring += (secs) => Console.WriteLine($"Expires in {secs}s");
/// await helper.RegisterAsync(credentials, cancellationToken);
/// // ... use voice connection ...
/// await helper.UnregisterAsync();
/// </code>
/// </para>
/// </summary>
public class SipRegistrationHelper : IDisposable
{
    private readonly object _lock = new();
    private SIPTransport? _sipTransport;
    private SIPRegistrationUserAgent? _registrationAgent;
    private SipConnectionCredentials? _credentials;
    private Timer? _expirationWarningTimer;
    private bool _disposed;
    private SipRegistrationState _state = SipRegistrationState.Unregistered;

    /// <summary>
    /// Default SIP transport port.
    /// </summary>
    public const int DefaultSipPort = 5060;

    /// <summary>
    /// Default registration expiry in seconds.
    /// </summary>
    public const int DefaultExpirySeconds = 300;

    /// <summary>
    /// Warning threshold before expiration (in seconds).
    /// </summary>
    public const int ExpirationWarningThresholdSeconds = 60;

    #region Properties

    /// <summary>
    /// Current registration state.
    /// </summary>
    public SipRegistrationState State
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

    /// <summary>
    /// The SIP username used for registration.
    /// </summary>
    public string? Username => _credentials?.Username;

    /// <summary>
    /// Whether currently registered with the SIP server.
    /// </summary>
    public bool IsRegistered => State == SipRegistrationState.Registered;

    /// <summary>
    /// Local SIP URI after registration.
    /// </summary>
    public string? LocalUri { get; private set; }

    #endregion

    #region Events

    /// <summary>
    /// Fired when registration state changes.
    /// </summary>
    public event Action<SipRegistrationState>? OnStateChanged;

    /// <summary>
    /// Fired when registration succeeds.
    /// </summary>
    public event Action? OnRegistered;

    /// <summary>
    /// Fired when registration fails.
    /// Parameters: (errorCode, errorMessage)
    /// </summary>
    public event Action<int, string>? OnRegistrationFailed;

    /// <summary>
    /// Fired when registration is about to expire.
    /// Parameter is seconds until expiration.
    /// </summary>
    public event Action<int>? OnRegistrationExpiring;

    /// <summary>
    /// Fired when unregistered (either voluntary or forced).
    /// </summary>
    public event Action? OnUnregistered;

    #endregion

    /// <summary>
    /// Registers with the SIP server using the provided credentials.
    /// </summary>
    /// <param name="credentials">SIP credentials from VoiceTierUpgradeEvent.</param>
    /// <param name="localPort">Local port for SIP transport. 0 for auto-selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if registration succeeded, false otherwise.</returns>
    /// <exception cref="ObjectDisposedException">If the helper has been disposed.</exception>
    /// <exception cref="InvalidOperationException">If already registered.</exception>
    public async Task<bool> RegisterAsync(
        SipConnectionCredentials credentials,
        int localPort = 0,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (State == SipRegistrationState.Registered || State == SipRegistrationState.Registering)
        {
            throw new InvalidOperationException("Already registered or registration in progress");
        }

        _credentials = credentials;
        State = SipRegistrationState.Registering;

        try
        {
            // Parse the registrar URI to get host and port
            var registrarUri = ParseRegistrarUri(credentials.Domain);

            // Create SIP transport
            _sipTransport = new SIPTransport();

            // Add UDP channel on specified or auto port
            var listenEndpoint = localPort > 0
                ? new IPEndPoint(IPAddress.Any, localPort)
                : new IPEndPoint(IPAddress.Any, 0); // Auto-select port

            _sipTransport.AddSIPChannel(new SIPUDPChannel(listenEndpoint));

            // Create SIP account credentials
            var sipAccountName = credentials.Username;
            var sipPassword = credentials.Password;
            var sipDomain = credentials.Domain;

            // Build the registration URI
            var registrarAddress = SIPURI.ParseSIPURI($"sip:{sipDomain}");

            // Create registration user agent
            _registrationAgent = new SIPRegistrationUserAgent(
                _sipTransport,
                sipAccountName,
                sipPassword,
                sipDomain,
                DefaultExpirySeconds);

            // Wire up events
            var registrationTcs = new TaskCompletionSource<bool>();

            _registrationAgent.RegistrationSuccessful += (uri, response) =>
            {
                LocalUri = uri?.ToString();
                State = SipRegistrationState.Registered;
                SetupExpirationWarningTimer();
                OnRegistered?.Invoke();
                registrationTcs.TrySetResult(true);
            };

            _registrationAgent.RegistrationFailed += (uri, response, errorMessage) =>
            {
                var statusCode = response?.StatusCode ?? 0;
                State = SipRegistrationState.Failed;
                OnRegistrationFailed?.Invoke(statusCode, errorMessage ?? response?.ReasonPhrase ?? "Unknown registration error");
                registrationTcs.TrySetResult(false);
            };

            _registrationAgent.RegistrationRemoved += (uri, response) =>
            {
                State = SipRegistrationState.Unregistered;
                StopExpirationWarningTimer();
                OnUnregistered?.Invoke();
            };

            // Start registration
            _registrationAgent.Start();

            // Wait for registration result or cancellation
            using var ctr = cancellationToken.Register(() => registrationTcs.TrySetCanceled());

            try
            {
                return await registrationTcs.Task;
            }
            catch (OperationCanceledException)
            {
                State = SipRegistrationState.Failed;
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            State = SipRegistrationState.Failed;
            OnRegistrationFailed?.Invoke(0, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Refreshes the SIP registration with new credentials if provided.
    /// </summary>
    /// <param name="newCredentials">Optional new credentials. If null, re-registers with existing credentials.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if refresh succeeded, false otherwise.</returns>
    public async Task<bool> RefreshAsync(
        SipConnectionCredentials? newCredentials = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (newCredentials != null)
        {
            // Need to unregister and re-register with new credentials
            await UnregisterAsync();
            return await RegisterAsync(newCredentials, cancellationToken: cancellationToken);
        }

        if (_registrationAgent == null || _credentials == null)
        {
            throw new InvalidOperationException("Not registered");
        }

        // Re-registration happens automatically via SIPRegistrationUserAgent
        // This method is here for explicit refresh if needed
        StopExpirationWarningTimer();
        SetupExpirationWarningTimer();

        return IsRegistered;
    }

    /// <summary>
    /// Unregisters from the SIP server.
    /// </summary>
    public Task UnregisterAsync()
    {
        ThrowIfDisposed();

        StopExpirationWarningTimer();

        if (_registrationAgent != null)
        {
            _registrationAgent.Stop();
            _registrationAgent = null;
        }

        if (_sipTransport != null)
        {
            _sipTransport.Shutdown();
            _sipTransport = null;
        }

        State = SipRegistrationState.Unregistered;
        _credentials = null;
        LocalUri = null;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the SIP transport for use by other components (e.g., for INVITE calls).
    /// </summary>
    /// <returns>The SIP transport, or null if not registered.</returns>
    public SIPTransport? GetTransport()
    {
        ThrowIfDisposed();
        return _sipTransport;
    }

    /// <summary>
    /// Gets the credentials used for registration.
    /// </summary>
    /// <returns>The SIP credentials, or null if not registered.</returns>
    public SipConnectionCredentials? GetCredentials()
    {
        ThrowIfDisposed();
        return _credentials;
    }

    #region Private Methods

    private static (string host, int port) ParseRegistrarUri(string domain)
    {
        // Domain might be "voice.bannou" or "voice.bannou:5060"
        if (domain.Contains(':'))
        {
            var parts = domain.Split(':');
            return (parts[0], int.TryParse(parts[1], out var port) ? port : DefaultSipPort);
        }
        return (domain, DefaultSipPort);
    }

    private void SetupExpirationWarningTimer()
    {
        StopExpirationWarningTimer();

        // Calculate when to fire warning (ExpirySeconds - WarningThreshold)
        var warningIntervalMs = (DefaultExpirySeconds - ExpirationWarningThresholdSeconds) * 1000;
        if (warningIntervalMs <= 0)
        {
            warningIntervalMs = DefaultExpirySeconds * 1000 / 2; // Warn at halfway if expiry is short
        }

        _expirationWarningTimer = new Timer(
            _ => OnRegistrationExpiring?.Invoke(ExpirationWarningThresholdSeconds),
            null,
            warningIntervalMs,
            Timeout.Infinite); // One-shot timer
    }

    private void StopExpirationWarningTimer()
    {
        _expirationWarningTimer?.Dispose();
        _expirationWarningTimer = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the helper and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _disposed = true;

                    StopExpirationWarningTimer();

                    _registrationAgent?.Stop();
                    _registrationAgent = null;

                    _sipTransport?.Shutdown();
                    _sipTransport = null;
                }
            }
        }
        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>
/// States for SIP registration lifecycle.
/// </summary>
public enum SipRegistrationState
{
    /// <summary>
    /// Not registered with SIP server.
    /// </summary>
    Unregistered,

    /// <summary>
    /// Registration in progress.
    /// </summary>
    Registering,

    /// <summary>
    /// Successfully registered with SIP server.
    /// </summary>
    Registered,

    /// <summary>
    /// Registration failed.
    /// </summary>
    Failed
}
