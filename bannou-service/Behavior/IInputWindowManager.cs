// =============================================================================
// Input Window Manager Interface
// Manages timed input windows for QTEs and player choices in cutscenes.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Manages input windows for player/behavior input during cutscenes.
/// </summary>
/// <remarks>
/// <para>
/// Input windows represent moments where specific participants can provide input.
/// The "blue/red/gray box" mental model from THE DREAM:
/// </para>
/// <list type="bullet">
/// <item><b>Blue box</b>: Your turn to input (you're the target)</item>
/// <item><b>Red box</b>: Their turn (waiting for another participant)</item>
/// <item><b>Gray box</b>: No input allowed (e.g., reading text)</item>
/// </list>
/// <para>
/// All input is server-adjudicated - client input is a "suggestion" that
/// may be accepted, modified, or replaced by timeout defaults.
/// </para>
/// </remarks>
public interface IInputWindowManager
{
    /// <summary>
    /// Creates a new input window.
    /// </summary>
    /// <param name="options">Window options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created window.</returns>
    Task<IInputWindow> CreateAsync(
        InputWindowOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Gets an active window by ID.
    /// </summary>
    /// <param name="windowId">The window ID.</param>
    /// <returns>The window, or null if not found or expired.</returns>
    IInputWindow? GetWindow(string windowId);

    /// <summary>
    /// Submits input to a window.
    /// </summary>
    /// <param name="windowId">The window ID.</param>
    /// <param name="entityId">The submitting entity.</param>
    /// <param name="input">The input value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The submission result.</returns>
    Task<InputSubmitResult> SubmitAsync(
        string windowId,
        Guid entityId,
        object input,
        CancellationToken ct = default);

    /// <summary>
    /// Closes a window without waiting for timeout.
    /// </summary>
    /// <param name="windowId">The window ID.</param>
    /// <param name="useDefault">Whether to use the default value.</param>
    void Close(string windowId, bool useDefault = true);

    /// <summary>
    /// Gets all active windows.
    /// </summary>
    IReadOnlyCollection<IInputWindow> ActiveWindows { get; }

    /// <summary>
    /// Gets windows waiting for a specific entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>Windows targeting this entity.</returns>
    IReadOnlyCollection<IInputWindow> GetWindowsForEntity(Guid entityId);

    /// <summary>
    /// Event raised when a window result is available.
    /// </summary>
    event EventHandler<InputWindowCompletedEventArgs>? WindowCompleted;

    /// <summary>
    /// Event raised when a window times out.
    /// </summary>
    event EventHandler<InputWindowTimedOutEventArgs>? WindowTimedOut;
}

/// <summary>
/// Represents an active input window.
/// </summary>
public interface IInputWindow
{
    /// <summary>
    /// Unique window identifier.
    /// </summary>
    string WindowId { get; }

    /// <summary>
    /// The entity that should provide input.
    /// </summary>
    Guid TargetEntity { get; }

    /// <summary>
    /// Type of input expected.
    /// </summary>
    InputWindowType WindowType { get; }

    /// <summary>
    /// Current window state.
    /// </summary>
    InputWindowState State { get; }

    /// <summary>
    /// When the window was created.
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// When the window expires.
    /// </summary>
    DateTime? ExpiresAt { get; }

    /// <summary>
    /// Time remaining until timeout.
    /// </summary>
    TimeSpan? TimeRemaining { get; }

    /// <summary>
    /// Available options (for Choice type).
    /// </summary>
    IReadOnlyList<InputOption>? Options { get; }

    /// <summary>
    /// The default value if timeout occurs.
    /// </summary>
    object? DefaultValue { get; }

    /// <summary>
    /// Source of the default value.
    /// </summary>
    DefaultValueSource DefaultSource { get; }

    /// <summary>
    /// The submitted input (null until submitted).
    /// </summary>
    object? SubmittedInput { get; }

    /// <summary>
    /// The final adjudicated value (null until resolved).
    /// </summary>
    object? FinalValue { get; }

    /// <summary>
    /// Whether input has been received.
    /// </summary>
    bool HasInput { get; }

    /// <summary>
    /// Whether the window has completed (input received or timed out).
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// Waits for the window to complete.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The final value.</returns>
    Task<object?> WaitForCompletionAsync(CancellationToken ct = default);
}

/// <summary>
/// Options for creating an input window.
/// </summary>
public sealed class InputWindowOptions
{
    /// <summary>
    /// The entity that should provide input.
    /// </summary>
    public Guid TargetEntity { get; init; }

    /// <summary>
    /// Type of input expected.
    /// </summary>
    public InputWindowType WindowType { get; init; } = InputWindowType.Choice;

    /// <summary>
    /// Timeout duration (null = indefinite).
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Available options (for Choice type).
    /// </summary>
    public IReadOnlyList<InputOption>? Options { get; init; }

    /// <summary>
    /// The default value if timeout occurs.
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// Source of the default value.
    /// </summary>
    public DefaultValueSource DefaultSource { get; init; } = DefaultValueSource.Cutscene;

    /// <summary>
    /// Optional sync point to emit when complete.
    /// </summary>
    public string? EmitSyncOnComplete { get; init; }

    /// <summary>
    /// Human-readable prompt text.
    /// </summary>
    public string? PromptText { get; init; }

    /// <summary>
    /// Custom window ID (auto-generated if null).
    /// </summary>
    public string? WindowId { get; init; }
}

/// <summary>
/// Type of input window.
/// </summary>
public enum InputWindowType
{
    /// <summary>Multiple choice selection.</summary>
    Choice,

    /// <summary>QTE button press.</summary>
    QuickTimeEvent,

    /// <summary>Directional input.</summary>
    Direction,

    /// <summary>Timing-based input.</summary>
    Timing,

    /// <summary>Free-form text input.</summary>
    Text,

    /// <summary>Confirmation (yes/no).</summary>
    Confirmation
}

/// <summary>
/// State of an input window.
/// </summary>
public enum InputWindowState
{
    /// <summary>Window is active and waiting for input.</summary>
    Waiting,

    /// <summary>Input was received and accepted.</summary>
    Submitted,

    /// <summary>Window timed out, using default.</summary>
    TimedOut,

    /// <summary>Window was closed without result.</summary>
    Closed
}

/// <summary>
/// Source of the default value for timeout.
/// </summary>
public enum DefaultValueSource
{
    /// <summary>Default defined in cutscene.</summary>
    Cutscene,

    /// <summary>Default from behavior stack evaluation.</summary>
    Behavior,

    /// <summary>No default (window stays open indefinitely).</summary>
    None
}

/// <summary>
/// An option in a choice input window.
/// </summary>
public sealed class InputOption
{
    /// <summary>
    /// The option value.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// Display label for the option.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this is the default option.
    /// </summary>
    public bool IsDefault { get; init; }
}

/// <summary>
/// Event args for window completion.
/// </summary>
public sealed class InputWindowCompletedEventArgs : EventArgs
{
    /// <summary>The window ID.</summary>
    public required string WindowId { get; init; }

    /// <summary>The target entity.</summary>
    public Guid TargetEntity { get; init; }

    /// <summary>The final value.</summary>
    public object? FinalValue { get; init; }

    /// <summary>Whether it was a timeout default.</summary>
    public bool WasDefault { get; init; }

    /// <summary>Time from creation to completion.</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Event args for window timeout.
/// </summary>
public sealed class InputWindowTimedOutEventArgs : EventArgs
{
    /// <summary>The window ID.</summary>
    public required string WindowId { get; init; }

    /// <summary>The target entity.</summary>
    public Guid TargetEntity { get; init; }

    /// <summary>The default value used.</summary>
    public object? DefaultValue { get; init; }

    /// <summary>The timeout duration.</summary>
    public TimeSpan Timeout { get; init; }
}
