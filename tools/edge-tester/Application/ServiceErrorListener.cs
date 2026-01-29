using BeyondImmersion.Bannou.Client;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BeyondImmersion.EdgeTester.Application;

/// <summary>
/// Listens for service.error events on an admin WebSocket connection.
/// Logs all errors and optionally triggers test suite termination.
/// </summary>
public sealed class ServiceErrorListener : IDisposable
{
    private readonly BannouClient _adminClient;
    private readonly bool _exitOnError;
    private readonly ConcurrentQueue<ServiceErrorInfo> _receivedErrors = new();
    private bool _isListening;

    /// <summary>
    /// Gets the count of service errors received since the listener was started.
    /// </summary>
    public int ErrorCount => _receivedErrors.Count;

    /// <summary>
    /// Gets all service errors received since the listener was started.
    /// </summary>
    public IReadOnlyList<ServiceErrorInfo> ReceivedErrors => _receivedErrors.ToList();

    /// <summary>
    /// Event raised when a service error is received.
    /// </summary>
    public event Action<ServiceErrorInfo>? OnServiceError;

    /// <summary>
    /// Creates a new service error listener.
    /// </summary>
    /// <param name="adminClient">The admin BannouClient to listen on.</param>
    /// <param name="exitOnError">If true, calls Environment.Exit(2) when an error is received.</param>
    public ServiceErrorListener(BannouClient adminClient, bool exitOnError)
    {
        _adminClient = adminClient ?? throw new ArgumentNullException(nameof(adminClient));
        _exitOnError = exitOnError;
    }

    /// <summary>
    /// Starts listening for service error events.
    /// </summary>
    public void StartListening()
    {
        if (_isListening)
            return;

        _isListening = true;

        // Register handler for service_error events
        _adminClient.OnEvent("service_error", HandleServiceError);

        Console.WriteLine("🔔 Service error listener started on admin connection");
    }

    /// <summary>
    /// Stops listening for service error events.
    /// </summary>
    public void StopListening()
    {
        if (!_isListening)
            return;

        _isListening = false;
        _adminClient.RemoveEventHandler("service_error");

        Console.WriteLine("🔕 Service error listener stopped");
    }

    /// <summary>
    /// Handles a service error event from the admin connection.
    /// </summary>
    private void HandleServiceError(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            var errorInfo = new ServiceErrorInfo
            {
                EventId = GetStringProperty(root, "eventId"),
                Timestamp = GetStringProperty(root, "timestamp"),
                ServiceId = GetStringProperty(root, "serviceId"),
                AppId = GetStringProperty(root, "appId"),
                Operation = GetStringProperty(root, "operation"),
                ErrorType = GetStringProperty(root, "errorType"),
                Message = GetStringProperty(root, "message"),
                Severity = GetStringProperty(root, "severity"),
                Dependency = GetStringProperty(root, "dependency"),
                Endpoint = GetStringProperty(root, "endpoint"),
                CorrelationId = GetStringProperty(root, "correlationId"),
                RawJson = payloadJson
            };

            _receivedErrors.Enqueue(errorInfo);

            // Log as error using Console.Error for visibility
            Console.Error.WriteLine();
            Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
            Console.Error.WriteLine($"🚨 SERVICE ERROR RECEIVED ({errorInfo.Severity?.ToUpperInvariant() ?? "UNKNOWN"})");
            Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
            Console.Error.WriteLine($"   Service:     {errorInfo.ServiceId}");
            Console.Error.WriteLine($"   App ID:      {errorInfo.AppId}");
            Console.Error.WriteLine($"   Operation:   {errorInfo.Operation}");
            Console.Error.WriteLine($"   Error Type:  {errorInfo.ErrorType}");
            Console.Error.WriteLine($"   Message:     {errorInfo.Message}");
            Console.Error.WriteLine($"   Endpoint:    {errorInfo.Endpoint}");
            Console.Error.WriteLine($"   Dependency:  {errorInfo.Dependency}");
            Console.Error.WriteLine($"   Correlation: {errorInfo.CorrelationId}");
            Console.Error.WriteLine($"   Event ID:    {errorInfo.EventId}");
            Console.Error.WriteLine($"   Timestamp:   {errorInfo.Timestamp}");
            Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
            Console.Error.WriteLine();

            // Raise event for any additional handlers
            OnServiceError?.Invoke(errorInfo);

            // Exit if configured to do so
            if (_exitOnError)
            {
                Console.Error.WriteLine("❌ EXIT_ON_SERVICE_ERROR is enabled - terminating test suite");
                Environment.Exit(2);
            }
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"⚠️ Failed to parse service error event: {ex.Message}");
            Console.Error.WriteLine($"   Raw payload: {payloadJson}");
        }
    }

    /// <summary>
    /// Helper to safely get a string property from a JsonElement.
    /// </summary>
    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }
        return null;
    }

    /// <summary>
    /// Prints a summary of all received errors.
    /// Call this at the end of the test run.
    /// </summary>
    public void PrintSummary()
    {
        if (_receivedErrors.IsEmpty)
        {
            Console.WriteLine("✅ No service errors received during test run");
            return;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.Error.WriteLine($"🚨 SERVICE ERROR SUMMARY: {_receivedErrors.Count} error(s) received");
        Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════════");

        var errorNumber = 0;
        foreach (var error in _receivedErrors)
        {
            errorNumber++;
            Console.Error.WriteLine($"  {errorNumber}. [{error.Severity}] {error.Operation} - {error.Message}");
            Console.Error.WriteLine($"     Service: {error.ServiceId}, Endpoint: {error.Endpoint}");
        }

        Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.Error.WriteLine();
    }

    /// <summary>
    /// Disposes resources and stops listening.
    /// </summary>
    public void Dispose()
    {
        StopListening();
    }
}

/// <summary>
/// Information about a service error event.
/// </summary>
public sealed class ServiceErrorInfo
{
    /// <summary>Unique identifier for this error event.</summary>
    public string? EventId { get; init; }

    /// <summary>When the error occurred.</summary>
    public string? Timestamp { get; init; }

    /// <summary>Service instance GUID.</summary>
    public string? ServiceId { get; init; }

    /// <summary>Application ID (e.g., "bannou").</summary>
    public string? AppId { get; init; }

    /// <summary>The operation/method where the error occurred.</summary>
    public string? Operation { get; init; }

    /// <summary>Classification of the error (e.g., "unexpected_exception").</summary>
    public string? ErrorType { get; init; }

    /// <summary>Human-readable error message.</summary>
    public string? Message { get; init; }

    /// <summary>Severity level (critical, error, warning).</summary>
    public string? Severity { get; init; }

    /// <summary>Implicated dependency (redis, pubsub, http:service, etc.).</summary>
    public string? Dependency { get; init; }

    /// <summary>The HTTP endpoint involved.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Request correlation ID for tracing.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The raw JSON payload.</summary>
    public string? RawJson { get; init; }
}
