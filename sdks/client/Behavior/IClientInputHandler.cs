// =============================================================================
// Client-Side Input Handler
// Game engine integration point for QTEs and player choices.
// =============================================================================

namespace BeyondImmersion.Bannou.Client.Behavior;

/// <summary>
/// Client-side input handler for QTEs and player choices.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface in your game engine to display input prompts
/// and collect player input during cutscenes and dialogues.
/// </para>
/// <para>
/// The "blue/red/gray box" mental model:
/// </para>
/// <list type="bullet">
/// <item><b>Blue box</b>: Your turn to input (local player is target)</item>
/// <item><b>Red box</b>: Their turn (waiting for another player)</item>
/// <item><b>Gray box</b>: No input allowed (cinematic in progress)</item>
/// </list>
/// <para>
/// All input is server-adjudicated - player input is a "suggestion" that
/// may be accepted, modified, or replaced by timeout defaults.
/// </para>
/// </remarks>
public interface IClientInputHandler
{
    /// <summary>
    /// Called when an input window opens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <paramref name="isForLocalPlayer"/> parameter determines the box color:
    /// </para>
    /// <list type="bullet">
    /// <item><c>true</c> = Blue box (player should provide input)</item>
    /// <item><c>false</c> = Red box (waiting for another player)</item>
    /// </list>
    /// <para>
    /// The game engine should display appropriate UI based on window type
    /// and start any timeout timers.
    /// </para>
    /// </remarks>
    /// <param name="window">The input window details.</param>
    /// <param name="isForLocalPlayer">True if this player should provide input.</param>
    void OnInputWindowOpened(ClientInputWindow window, bool isForLocalPlayer);

    /// <summary>
    /// Called when an input window closes.
    /// </summary>
    /// <remarks>
    /// The game engine should hide the input UI and may display feedback
    /// based on the result (success, timeout, rejected).
    /// </remarks>
    /// <param name="windowId">The window ID.</param>
    /// <param name="result">The final result.</param>
    void OnInputWindowClosed(string windowId, ClientInputResult result);

    /// <summary>
    /// Called to update the remaining time on an input window.
    /// </summary>
    /// <param name="windowId">The window ID.</param>
    /// <param name="remaining">Time remaining before timeout.</param>
    void OnInputWindowTimeUpdate(string windowId, TimeSpan remaining);

    /// <summary>
    /// Submits player input for an active window.
    /// </summary>
    /// <remarks>
    /// This method sends input to the server for adjudication.
    /// The server may accept, reject, or modify the input.
    /// </remarks>
    /// <param name="windowId">The window ID.</param>
    /// <param name="input">The player's input value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The server-adjudicated result.</returns>
    Task<ClientInputResult> SubmitInputAsync(
        string windowId,
        object input,
        CancellationToken ct = default);
}

/// <summary>
/// Type of input window.
/// </summary>
public enum ClientInputWindowType
{
    /// <summary>Multiple choice selection.</summary>
    Choice,

    /// <summary>QTE button press.</summary>
    QuickTimeEvent,

    /// <summary>Directional input (e.g., dodge direction).</summary>
    Direction,

    /// <summary>Timing-based input (e.g., hit at right moment).</summary>
    Timing,

    /// <summary>Free-form text input.</summary>
    Text,

    /// <summary>Yes/no confirmation.</summary>
    Confirmation
}

/// <summary>
/// Client-side representation of an input window.
/// </summary>
public sealed class ClientInputWindow
{
    /// <summary>
    /// Unique window identifier.
    /// </summary>
    public required string WindowId { get; init; }

    /// <summary>
    /// Type of input expected.
    /// </summary>
    public ClientInputWindowType WindowType { get; init; }

    /// <summary>
    /// Human-readable prompt text.
    /// </summary>
    public string? PromptText { get; init; }

    /// <summary>
    /// Available options (for Choice type).
    /// </summary>
    public IReadOnlyList<ClientInputOption>? Options { get; init; }

    /// <summary>
    /// Time remaining until timeout (null = indefinite).
    /// </summary>
    public TimeSpan? TimeRemaining { get; init; }

    /// <summary>
    /// The entity this input is for.
    /// </summary>
    public Guid TargetEntity { get; init; }

    /// <summary>
    /// Optional button hint for QTE (e.g., "A", "X", "Space").
    /// </summary>
    public string? ButtonHint { get; init; }

    /// <summary>
    /// Whether this is a forced input (blocks gameplay until resolved).
    /// </summary>
    public bool IsForced { get; init; }
}

/// <summary>
/// A selectable option in a choice input window.
/// </summary>
public sealed class ClientInputOption
{
    /// <summary>
    /// The option value (sent back to server).
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// Display label for the option.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Optional description/tooltip.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this is the default option (used on timeout).
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Whether this option is currently available.
    /// </summary>
    public bool IsAvailable { get; init; } = true;

    /// <summary>
    /// Reason why option is unavailable (if applicable).
    /// </summary>
    public string? UnavailableReason { get; init; }
}

/// <summary>
/// Result of a client input submission.
/// </summary>
public sealed class ClientInputResult
{
    /// <summary>
    /// Whether the input was accepted by the server.
    /// </summary>
    public bool Accepted { get; init; }

    /// <summary>
    /// The final adjudicated value (may differ from submitted).
    /// </summary>
    public object? FinalValue { get; init; }

    /// <summary>
    /// Whether this result came from timeout (used default).
    /// </summary>
    public bool WasTimeout { get; init; }

    /// <summary>
    /// Whether this result came from behavior default (QTE computed answer).
    /// </summary>
    public bool WasBehaviorDefault { get; init; }

    /// <summary>
    /// Reason if input was rejected.
    /// </summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// Time from window open to result.
    /// </summary>
    public TimeSpan ResponseTime { get; init; }

    /// <summary>
    /// Creates an accepted result.
    /// </summary>
    public static ClientInputResult Accept(object value, TimeSpan responseTime) => new()
    {
        Accepted = true,
        FinalValue = value,
        ResponseTime = responseTime
    };

    /// <summary>
    /// Creates a rejected result.
    /// </summary>
    public static ClientInputResult Reject(string reason) => new()
    {
        Accepted = false,
        RejectionReason = reason
    };

    /// <summary>
    /// Creates a timeout result.
    /// </summary>
    public static ClientInputResult Timeout(object? defaultValue, TimeSpan duration) => new()
    {
        Accepted = true,
        FinalValue = defaultValue,
        WasTimeout = true,
        ResponseTime = duration
    };

    /// <summary>
    /// Creates a behavior default result.
    /// </summary>
    public static ClientInputResult BehaviorDefault(object? defaultValue, TimeSpan duration) => new()
    {
        Accepted = true,
        FinalValue = defaultValue,
        WasBehaviorDefault = true,
        ResponseTime = duration
    };
}
