// ═══════════════════════════════════════════════════════════════════════════
// ABML Error Information
// Structured error information for error handling.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Abml.Execution;

/// <summary>
/// Structured error information for ABML error handlers.
/// </summary>
/// <param name="Message">The error message.</param>
/// <param name="FlowName">The flow where the error occurred.</param>
/// <param name="ActionType">The action type that caused the error.</param>
/// <param name="ChannelName">The channel name (for multi-channel execution).</param>
/// <param name="Exception">The underlying exception, if any.</param>
public sealed record ErrorInfo(
    string Message,
    string FlowName,
    string ActionType,
    string? ChannelName = null,
    Exception? Exception = null)
{
    /// <summary>
    /// Creates an ErrorInfo from an ActionResult.Error.
    /// </summary>
    public static ErrorInfo FromErrorResult(
        string message,
        string flowName,
        string actionType,
        string? channelName = null) =>
        new(message, flowName, actionType, channelName);

    /// <summary>
    /// Creates an ErrorInfo from an exception.
    /// </summary>
    public static ErrorInfo FromException(
        Exception ex,
        string flowName,
        string actionType,
        string? channelName = null) =>
        new(ex.Message, flowName, actionType, channelName, ex);

    /// <summary>
    /// Gets a dictionary representation for use as _error variable.
    /// </summary>
    public Dictionary<string, object?> ToDictionary() => new()
    {
        ["message"] = Message,
        ["flow"] = FlowName,
        ["action"] = ActionType,
        ["channel"] = ChannelName,
        ["type"] = Exception?.GetType().Name
    };
}
