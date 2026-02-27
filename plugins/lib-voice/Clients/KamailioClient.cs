using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Voice.Clients;

/// <summary>
/// HTTP client for Kamailio SIP proxy health checking.
/// Thread-safe implementation suitable for multi-instance deployments (FOUNDATION TENETS).
/// </summary>
public class KamailioClient : IKamailioClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<KamailioClient> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly string _healthEndpoint;
    private readonly TimeSpan _requestTimeout;

    /// <summary>
    /// Initializes a new instance of the KamailioClient.
    /// </summary>
    /// <param name="httpClient">HTTP client for health check requests.</param>
    /// <param name="host">Kamailio host address.</param>
    /// <param name="port">Kamailio JSONRPC port (default 5080).</param>
    /// <param name="requestTimeout">Timeout for Kamailio HTTP requests.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public KamailioClient(
        HttpClient httpClient,
        string host,
        int port,
        TimeSpan requestTimeout,
        ILogger<KamailioClient> logger,
        ITelemetryProvider telemetryProvider)
    {
        _httpClient = httpClient;
        _logger = logger;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
        _healthEndpoint = $"http://{host}:{port}/health";
        _requestTimeout = requestTimeout;
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "KamailioClient.IsHealthyAsync");
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_requestTimeout);

            var response = await _httpClient.GetAsync(
                _healthEndpoint,
                cts.Token);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Kamailio health check failed");
            return false;
        }
    }
}
