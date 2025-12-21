using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Voice.Clients;

/// <summary>
/// UDP client for RTPEngine ng control protocol.
/// Thread-safe implementation suitable for multi-instance deployments (Tenet 4).
/// Uses cookie-prefixed JSON messages as per ng protocol specification.
/// </summary>
public class RtpEngineClient : IRtpEngineClient
{
    private readonly UdpClient _client;
    private readonly IPEndPoint _endpoint;
    private readonly ILogger<RtpEngineClient> _logger;
    private readonly TimeSpan _timeout;
    private readonly object _sendLock = new();
    private long _cookieCounter;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes a new instance of the RtpEngineClient.
    /// </summary>
    /// <param name="host">RTPEngine host address.</param>
    /// <param name="port">RTPEngine ng protocol port (default 22222).</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeoutSeconds">Timeout for responses in seconds (default 5).</param>
    public RtpEngineClient(
        string host,
        int port,
        ILogger<RtpEngineClient> logger,
        int timeoutSeconds = 5)
    {
        if (string.IsNullOrEmpty(host))
        {
            throw new ArgumentException("Host cannot be null or empty", nameof(host));
        }

        _client = new UdpClient();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // Resolve hostname to IP address (IPAddress.Parse only handles numeric IPs)
        if (!IPAddress.TryParse(host, out var ipAddress))
        {
            try
            {
                var addresses = Dns.GetHostAddresses(host);
                if (addresses.Length == 0)
                {
                    throw new ArgumentException($"Could not resolve hostname: {host}", nameof(host));
                }
                ipAddress = addresses[0];
            }
            catch (SocketException ex)
            {
                throw new ArgumentException($"Could not resolve hostname: {host}", nameof(host), ex);
            }
        }
        _endpoint = new IPEndPoint(ipAddress, port);
    }

    /// <inheritdoc />
    public async Task<RtpEngineOfferResponse> OfferAsync(
        string callId,
        string fromTag,
        string sdp,
        string[]? flags = null,
        CancellationToken cancellationToken = default)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "offer",
            ["call-id"] = callId,
            ["from-tag"] = fromTag,
            ["sdp"] = sdp
        };

        if (flags != null && flags.Length > 0)
        {
            command["flags"] = flags;
        }

        var response = await SendCommandAsync(command, cancellationToken);
        return ParseResponse<RtpEngineOfferResponse>(response);
    }

    /// <inheritdoc />
    public async Task<RtpEngineAnswerResponse> AnswerAsync(
        string callId,
        string fromTag,
        string toTag,
        string sdp,
        CancellationToken cancellationToken = default)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "answer",
            ["call-id"] = callId,
            ["from-tag"] = fromTag,
            ["to-tag"] = toTag,
            ["sdp"] = sdp
        };

        var response = await SendCommandAsync(command, cancellationToken);
        return ParseResponse<RtpEngineAnswerResponse>(response);
    }

    /// <inheritdoc />
    public async Task<RtpEngineDeleteResponse> DeleteAsync(
        string callId,
        string fromTag,
        CancellationToken cancellationToken = default)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "delete",
            ["call-id"] = callId,
            ["from-tag"] = fromTag
        };

        var response = await SendCommandAsync(command, cancellationToken);
        return ParseResponse<RtpEngineDeleteResponse>(response);
    }

    /// <inheritdoc />
    public async Task<RtpEnginePublishResponse> PublishAsync(
        string callId,
        string fromTag,
        string sdp,
        CancellationToken cancellationToken = default)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "publish",
            ["call-id"] = callId,
            ["from-tag"] = fromTag,
            ["sdp"] = sdp
        };

        var response = await SendCommandAsync(command, cancellationToken);
        return ParseResponse<RtpEnginePublishResponse>(response);
    }

    /// <inheritdoc />
    public async Task<RtpEngineSubscribeResponse> SubscribeRequestAsync(
        string callId,
        string[] fromTags,
        string subscriberLabel,
        CancellationToken cancellationToken = default)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "subscribe request",
            ["call-id"] = callId,
            ["from-tags"] = fromTags,
            ["set-label"] = subscriberLabel
        };

        var response = await SendCommandAsync(command, cancellationToken);
        return ParseResponse<RtpEngineSubscribeResponse>(response);
    }

    /// <inheritdoc />
    public async Task<RtpEngineQueryResponse> QueryAsync(
        string callId,
        CancellationToken cancellationToken = default)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "query",
            ["call-id"] = callId
        };

        var response = await SendCommandAsync(command, cancellationToken);
        return ParseResponse<RtpEngineQueryResponse>(response);
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var command = new Dictionary<string, object>
            {
                ["command"] = "ping"
            };

            var response = await SendCommandAsync(command, cancellationToken);
            return response.Contains("pong", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RTPEngine health check failed");
            return false;
        }
    }

    private async Task<string> SendCommandAsync(
        Dictionary<string, object> command,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Generate unique cookie for this request
        var cookie = $"{Interlocked.Increment(ref _cookieCounter)}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        // Serialize command to JSON
        var jsonCommand = JsonSerializer.Serialize(command, JsonOptions);

        // Build message: "cookie json-data"
        var message = $"{cookie} {jsonCommand}";
        var data = Encoding.UTF8.GetBytes(message);

        _logger.LogDebug("RTPEngine command: {Cookie} {Command}", cookie, jsonCommand);

        // UDP send is not thread-safe, use lock
        lock (_sendLock)
        {
            _client.Send(data, data.Length, _endpoint);
        }

        // Receive response with timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            var result = await _client.ReceiveAsync(cts.Token);
            var response = Encoding.UTF8.GetString(result.Buffer);

            _logger.LogDebug("RTPEngine response: {Response}", response);

            // Parse response - skip cookie prefix
            var spaceIndex = response.IndexOf(' ');
            if (spaceIndex > 0)
            {
                var responseCookie = response[..spaceIndex];
                if (responseCookie != cookie)
                {
                    _logger.LogWarning("RTPEngine response cookie mismatch: expected {Expected}, got {Actual}",
                        cookie, responseCookie);
                }
                response = response[(spaceIndex + 1)..];
            }

            return response;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"RTPEngine command '{command["command"]}' timed out after {_timeout.TotalSeconds}s");
        }
    }

    private T ParseResponse<T>(string json) where T : RtpEngineBaseResponse, new()
    {
        try
        {
            var response = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (response == null)
            {
                return new T { Result = "error", ErrorReason = "Failed to parse response" };
            }
            return response;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse RTPEngine response: {Json}", json);
            return new T { Result = "error", ErrorReason = ex.Message };
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
