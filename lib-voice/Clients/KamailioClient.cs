using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Voice.Clients;

/// <summary>
/// HTTP client for Kamailio JSONRPC 2.0 control protocol.
/// Thread-safe implementation suitable for multi-instance deployments (FOUNDATION TENETS).
/// </summary>
public class KamailioClient : IKamailioClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<KamailioClient> _logger;
    private readonly IMessageBus _messageBus;
    private readonly string _rpcEndpoint;
    private long _requestId;

    // NOTE: Kamailio JSONRPC API uses snake_case - intentionally NOT using BannouJson.Options
    // which is designed for internal Bannou service communication
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes a new instance of the KamailioClient.
    /// </summary>
    /// <param name="httpClient">HTTP client for JSONRPC requests.</param>
    /// <param name="host">Kamailio host address.</param>
    /// <param name="port">Kamailio JSONRPC port (default 5080).</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="messageBus">Message bus for error event publishing.</param>
    public KamailioClient(
        HttpClient httpClient,
        string host,
        int port,
        ILogger<KamailioClient> logger,
        IMessageBus messageBus)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _rpcEndpoint = $"http://{host}:{port}/RPC";
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ActiveDialog>> GetActiveDialogsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await CallRpcAsync<DialogListResponse>("dlg.list", cancellationToken);
            if (response?.Dialogs == null)
            {
                return Enumerable.Empty<ActiveDialog>();
            }

            return response.Dialogs.Select(d => new ActiveDialog
            {
                DialogId = d.HashEntry ?? string.Empty,
                CallId = d.CallId ?? string.Empty,
                FromTag = d.FromTag ?? string.Empty,
                ToTag = d.ToTag ?? string.Empty,
                FromUri = d.FromUri ?? string.Empty,
                ToUri = d.ToUri ?? string.Empty,
                State = d.State ?? string.Empty,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(d.StartTime)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active dialogs from Kamailio");
            await _messageBus.TryPublishErrorAsync(
                "voice",
                "GetActiveDialogs",
                ex.GetType().Name,
                ex.Message,
                dependency: "kamailio",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return Enumerable.Empty<ActiveDialog>();
        }
    }

    /// <inheritdoc />
    public async Task<bool> TerminateDialogAsync(string dialogId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(dialogId))
        {
            throw new ArgumentException("Dialog ID cannot be null or empty", nameof(dialogId));
        }

        try
        {
            var response = await CallRpcAsync<BaseJsonRpcResult>(
                "dlg.end_dlg",
                cancellationToken,
                dialogId);

            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to terminate dialog {DialogId}", dialogId);
            await _messageBus.TryPublishErrorAsync(
                "voice",
                "TerminateDialog",
                ex.GetType().Name,
                ex.Message,
                dependency: "kamailio",
                details: new { dialogId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReloadDispatcherAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await CallRpcAsync<BaseJsonRpcResult>("dispatcher.reload", cancellationToken);
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload Kamailio dispatcher");
            await _messageBus.TryPublishErrorAsync(
                "voice",
                "ReloadDispatcher",
                ex.GetType().Name,
                ex.Message,
                dependency: "kamailio",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<KamailioStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await CallRpcAsync<StatsResponse>(
                "stats.get_statistics",
                cancellationToken,
                "all");

            if (response == null)
            {
                return new KamailioStats();
            }

            return new KamailioStats
            {
                ActiveDialogs = response.GetStat("dialog", "active_dialogs"),
                CurrentTransactions = response.GetStat("tm", "current"),
                TotalReceivedRequests = response.GetStat("core", "rcv_requests"),
                TotalReceivedReplies = response.GetStat("core", "rcv_replies"),
                UptimeSeconds = response.GetStat("core", "uptime"),
                MemoryUsed = response.GetStat("shmem", "used_size")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Kamailio stats");
            await _messageBus.TryPublishErrorAsync(
                "voice",
                "GetStats",
                ex.GetType().Name,
                ex.Message,
                dependency: "kamailio",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return new KamailioStats();
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await _httpClient.GetAsync(
                _rpcEndpoint.Replace("/RPC", "/health"),
                cts.Token);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Kamailio health check failed");
            return false;
        }
    }

    private async Task<T?> CallRpcAsync<T>(
        string method,
        CancellationToken cancellationToken,
        params object[] parameters)
        where T : class
    {
        var requestId = Interlocked.Increment(ref _requestId);

        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = requestId,
            Method = method,
            Params = parameters.Length > 0 ? parameters : null
        };

        _logger.LogDebug("Kamailio JSONRPC request: {Method} (id={RequestId})", method, requestId);

        var response = await _httpClient.PostAsJsonAsync(_rpcEndpoint, request, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Kamailio JSONRPC failed: {StatusCode}", response.StatusCode);
            return null;
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<JsonRpcResponse<T>>(JsonOptions, cancellationToken);

        if (jsonResponse?.Error != null)
        {
            _logger.LogWarning("Kamailio JSONRPC error: {Code} - {Message}",
                jsonResponse.Error.Code, jsonResponse.Error.Message);
            return null;
        }

        return jsonResponse?.Result;
    }

    #region JSONRPC Models

    private class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; init; } = "2.0";

        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("method")]
        public string Method { get; init; } = string.Empty;

        [JsonPropertyName("params")]
        public object[]? Params { get; init; }
    }

    private class JsonRpcResponse<T>
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; init; } = string.Empty;

        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("result")]
        public T? Result { get; init; }

        [JsonPropertyName("error")]
        public JsonRpcError? Error { get; init; }
    }

    private class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; init; }

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;
    }

    private class BaseJsonRpcResult
    {
        [JsonPropertyName("result")]
        public string? Result { get; init; }
    }

    private class DialogListResponse
    {
        [JsonPropertyName("Dialogs")]
        public List<DialogInfo>? Dialogs { get; init; }
    }

    private class DialogInfo
    {
        [JsonPropertyName("hash_entry")]
        public string? HashEntry { get; init; }

        [JsonPropertyName("call-id")]
        public string? CallId { get; init; }

        [JsonPropertyName("from_tag")]
        public string? FromTag { get; init; }

        [JsonPropertyName("to_tag")]
        public string? ToTag { get; init; }

        [JsonPropertyName("from_uri")]
        public string? FromUri { get; init; }

        [JsonPropertyName("to_uri")]
        public string? ToUri { get; init; }

        [JsonPropertyName("state")]
        public string? State { get; init; }

        [JsonPropertyName("start_time")]
        public long StartTime { get; init; }
    }

    private class StatsResponse
    {
        [JsonPropertyName("result")]
        public Dictionary<string, Dictionary<string, long>>? Stats { get; init; }

        public int GetStat(string group, string name)
        {
            if (Stats == null) return 0;
            if (!Stats.TryGetValue(group, out var groupStats)) return 0;
            if (!groupStats.TryGetValue(name, out var value)) return 0;
            return (int)Math.Min(value, int.MaxValue);
        }
    }

    #endregion
}
