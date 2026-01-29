using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BeyondImmersion.BannouService.Voice.Clients;

/// <summary>
/// UDP client for RTPEngine ng control protocol.
/// Thread-safe implementation suitable for multi-instance deployments (FOUNDATION TENETS).
/// Uses cookie-prefixed bencode messages as per ng protocol specification.
/// </summary>
public class RtpEngineClient : IRtpEngineClient
{
    private readonly UdpClient _client;
    private readonly IPEndPoint _endpoint;
    private readonly ILogger<RtpEngineClient> _logger;
    private readonly IMessageBus _messageBus;
    private readonly TimeSpan _timeout;
    private readonly object _sendLock = new();
    private long _cookieCounter;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the RtpEngineClient.
    /// </summary>
    /// <param name="host">RTPEngine host address.</param>
    /// <param name="port">RTPEngine ng protocol port.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="messageBus">Message bus for error event publishing.</param>
    /// <param name="timeoutSeconds">Timeout for responses in seconds.</param>
    public RtpEngineClient(
        string host,
        int port,
        ILogger<RtpEngineClient> logger,
        IMessageBus messageBus,
        int timeoutSeconds)
    {
        if (string.IsNullOrEmpty(host))
        {
            throw new ArgumentException("Host cannot be null or empty", nameof(host));
        }

        _client = new UdpClient();
        _logger = logger;
        _messageBus = messageBus;
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
        return await ParseBencodeResponse<RtpEngineOfferResponse>(response);
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
        return await ParseBencodeResponse<RtpEngineAnswerResponse>(response);
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
        return await ParseBencodeResponse<RtpEngineDeleteResponse>(response);
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
        return await ParseBencodeResponse<RtpEnginePublishResponse>(response);
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
        return await ParseBencodeResponse<RtpEngineSubscribeResponse>(response);
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
        return await ParseBencodeResponse<RtpEngineQueryResponse>(response);
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

            // Parse bencode response for "pong" result
            // Response format: d6:result4:ponge
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

        // Serialize command to bencode format
        var bencodeCommand = EncodeBencode(command);

        // Build message: "cookie bencode-data"
        var message = $"{cookie} {bencodeCommand}";
        var data = Encoding.UTF8.GetBytes(message);

        _logger.LogDebug("RTPEngine command: {Cookie} {Command}", cookie, bencodeCommand);

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
                    _logger.LogError("RTPEngine response cookie mismatch: expected {Expected}, got {Actual}",
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

    /// <summary>
    /// Encode a dictionary to bencode format.
    /// Bencode format: d{key1}{value1}{key2}{value2}...e
    /// Keys must be sorted alphabetically.
    /// </summary>
    private static string EncodeBencode(Dictionary<string, object> dict)
    {
        var sb = new StringBuilder();
        sb.Append('d');

        // Keys must be sorted in bencode dictionaries
        foreach (var key in dict.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var value = dict[key];
            sb.Append(EncodeString(key));
            sb.Append(EncodeValue(value));
        }

        sb.Append('e');
        return sb.ToString();
    }

    /// <summary>
    /// Encode a value to bencode format.
    /// </summary>
    private static string EncodeValue(object value)
    {
        return value switch
        {
            string s => EncodeString(s),
            int i => $"i{i}e",
            long l => $"i{l}e",
            bool b => $"i{(b ? 1 : 0)}e",
            string[] arr => EncodeList(arr),
            IEnumerable<string> enumerable => EncodeList(enumerable.ToArray()),
            Dictionary<string, object> dict => EncodeBencode(dict),
            // Defensive: some object ToString() can return null; empty string is safe for bencode
            _ => EncodeString(value?.ToString() ?? string.Empty)
        };
    }

    /// <summary>
    /// Encode a string to bencode format: length:content
    /// </summary>
    private static string EncodeString(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        return $"{bytes.Length}:{s}";
    }

    /// <summary>
    /// Encode a list to bencode format: l{item1}{item2}...e
    /// </summary>
    private static string EncodeList(string[] items)
    {
        var sb = new StringBuilder();
        sb.Append('l');
        foreach (var item in items)
        {
            sb.Append(EncodeString(item));
        }
        sb.Append('e');
        return sb.ToString();
    }

    /// <summary>
    /// Parse bencode response to extract key-value pairs.
    /// Simple parser for RTPEngine responses.
    /// </summary>
    private static Dictionary<string, string> DecodeBencode(string bencode)
    {
        var result = new Dictionary<string, string>();

        if (string.IsNullOrEmpty(bencode) || bencode[0] != 'd')
        {
            return result;
        }

        var index = 1; // Skip 'd'
        while (index < bencode.Length && bencode[index] != 'e')
        {
            // Parse key (always a string)
            var (key, keyEndIndex) = ParseBencodeString(bencode, index);
            if (key == null) break;
            index = keyEndIndex;

            // Parse value
            var (value, valueEndIndex) = ParseBencodeValue(bencode, index);
            if (value != null)
            {
                result[key] = value;
            }
            index = valueEndIndex;
        }

        return result;
    }

    /// <summary>
    /// Parse a bencode string at the given index.
    /// Returns (value, nextIndex) or (null, index) on failure.
    /// </summary>
    private static (string? Value, int NextIndex) ParseBencodeString(string bencode, int index)
    {
        if (index >= bencode.Length) return (null, index);

        // Find the colon
        var colonIndex = bencode.IndexOf(':', index);
        if (colonIndex < 0) return (null, index);

        // Parse length
        if (!int.TryParse(bencode.AsSpan(index, colonIndex - index), out var length))
        {
            return (null, index);
        }

        // Extract string
        var startIndex = colonIndex + 1;
        if (startIndex + length > bencode.Length) return (null, index);

        var value = bencode.Substring(startIndex, length);
        return (value, startIndex + length);
    }

    /// <summary>
    /// Parse a bencode value at the given index.
    /// Returns (value, nextIndex) or (null, nextIndex) on skip/failure.
    /// </summary>
    private static (string? Value, int NextIndex) ParseBencodeValue(string bencode, int index)
    {
        if (index >= bencode.Length) return (null, index);

        var c = bencode[index];

        // Integer: i<number>e
        if (c == 'i')
        {
            var endIndex = bencode.IndexOf('e', index + 1);
            if (endIndex < 0) return (null, index);
            var value = bencode.Substring(index + 1, endIndex - index - 1);
            return (value, endIndex + 1);
        }

        // String: length:content
        if (char.IsDigit(c))
        {
            return ParseBencodeString(bencode, index);
        }

        // List: l...e - skip for now (return null but advance past it)
        if (c == 'l')
        {
            var depth = 1;
            var i = index + 1;
            while (i < bencode.Length && depth > 0)
            {
                if (bencode[i] == 'l' || bencode[i] == 'd') depth++;
                else if (bencode[i] == 'e') depth--;
                else if (char.IsDigit(bencode[i]))
                {
                    // Skip string in list
                    var (_, nextIdx) = ParseBencodeString(bencode, i);
                    i = nextIdx;
                    continue;
                }
                i++;
            }
            return (null, i);
        }

        // Dictionary: d...e - skip for now
        if (c == 'd')
        {
            var depth = 1;
            var i = index + 1;
            while (i < bencode.Length && depth > 0)
            {
                if (bencode[i] == 'l' || bencode[i] == 'd') depth++;
                else if (bencode[i] == 'e') depth--;
                else if (char.IsDigit(bencode[i]))
                {
                    // Skip string
                    var (_, nextIdx) = ParseBencodeString(bencode, i);
                    i = nextIdx;
                    continue;
                }
                i++;
            }
            return (null, i);
        }

        return (null, index + 1);
    }

    private async Task<T> ParseBencodeResponse<T>(string bencode) where T : RtpEngineBaseResponse, new()
    {
        try
        {
            var dict = DecodeBencode(bencode);
            var response = new T();

            if (dict.TryGetValue("result", out var result))
            {
                response.Result = result;
            }

            if (dict.TryGetValue("error-reason", out var errorReason))
            {
                response.ErrorReason = errorReason;
            }

            if (dict.TryGetValue("sdp", out var sdp))
            {
                if (response is RtpEngineOfferResponse offerResponse)
                {
                    offerResponse.Sdp = sdp;
                }
                else if (response is RtpEngineAnswerResponse answerResponse)
                {
                    answerResponse.Sdp = sdp;
                }
                else if (response is RtpEngineSubscribeResponse subscribeResponse)
                {
                    subscribeResponse.Sdp = sdp;
                }
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse RTPEngine bencode response: {Bencode}", bencode);
            await _messageBus.TryPublishErrorAsync(
                "voice",
                "ParseBencodeResponse",
                ex.GetType().Name,
                ex.Message,
                dependency: "rtpengine",
                details: new { bencode },
                stack: ex.StackTrace);
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
