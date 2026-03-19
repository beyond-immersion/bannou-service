#nullable enable

namespace BeyondImmersion.Bannou.Core;

/// <summary>
/// Definition of a prebound API call.
/// Decoupled from generated contract models for use across services.
/// </summary>
/// <remarks>
/// This model is used by ServiceNavigator.ExecutePreboundApiAsync to define
/// what service/endpoint to call and what payload template to use.
/// </remarks>
public class PreboundApi
{
    /// <summary>
    /// Target service name (e.g., "currency", "inventory", "character").
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// Target endpoint path (e.g., "/currency/transfer", "/inventory/add").
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// JSON payload template with {{variable}} placeholders.
    /// Variables are substituted from the context dictionary at execution time.
    /// </summary>
    public string PayloadTemplate { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable description of what this API call does.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// How to execute this API call.
    /// </summary>
    public PreboundApiExecutionMode ExecutionMode { get; init; } = PreboundApiExecutionMode.Sync;

    /// <summary>
    /// Optional transformation rules for the API response.
    /// When set, the raw response is evaluated against these rules to produce
    /// a transformed result (potentially different status code and payload).
    /// When null, the raw response passes through unchanged.
    /// </summary>
    public ResponseTransformation? ResponseTransformation { get; init; }
}

/// <summary>
/// How a prebound API should be executed.
/// </summary>
public enum PreboundApiExecutionMode
{
    /// <summary>Execute synchronously and wait for response.</summary>
    Sync,

    /// <summary>Execute asynchronously but still track completion.</summary>
    Async,

    /// <summary>Execute and don't wait or track - fire and forget.</summary>
    FireAndForget
}
