// =============================================================================
// Input Window Manager
// Manages timed input windows for QTEs and player choices in cutscenes.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Behavior.Coordination;

/// <summary>
/// Default implementation of input window management.
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe implementation that manages timed input windows for
/// player/behavior input during cutscenes. Features:
/// </para>
/// <list type="bullet">
/// <item>Automatic timeout handling with configurable defaults</item>
/// <item>Server-side adjudication of all input</item>
/// <item>Support for various input types (choice, QTE, direction, etc.)</item>
/// <item>Integration with behavior stack for default values</item>
/// </list>
/// </remarks>
public sealed class InputWindowManager : IInputWindowManager, IDisposable
{
    private readonly ConcurrentDictionary<string, InputWindowImpl> _windows;
    private readonly TimeSpan _defaultTimeout;
    private readonly Func<Guid, object?>? _behaviorDefaultResolver;
    private readonly ILogger<InputWindowManager>? _logger;
    private readonly ITelemetryProvider? _telemetryProvider;
    private readonly CancellationTokenSource _disposeCts;
    private int _windowIdCounter;
    private bool _disposed;

    /// <summary>
    /// Creates a new input window manager.
    /// </summary>
    /// <param name="defaultTimeout">Default timeout for windows.</param>
    /// <param name="behaviorDefaultResolver">Optional function to get behavior defaults.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="telemetryProvider">Optional telemetry provider for span instrumentation.</param>
    public InputWindowManager(
        TimeSpan defaultTimeout,
        Func<Guid, object?>? behaviorDefaultResolver = null,
        ILogger<InputWindowManager>? logger = null,
        ITelemetryProvider? telemetryProvider = null)
    {
        _defaultTimeout = defaultTimeout;
        _behaviorDefaultResolver = behaviorDefaultResolver;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
        _windows = new ConcurrentDictionary<string, InputWindowImpl>(StringComparer.OrdinalIgnoreCase);
        _disposeCts = new CancellationTokenSource();
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<IInputWindow> ActiveWindows =>
        _windows.Values
            .Where(w => w.State == InputWindowState.Waiting)
            .Cast<IInputWindow>()
            .ToList()
            .AsReadOnly();

    /// <inheritdoc/>
    public event EventHandler<InputWindowCompletedEventArgs>? WindowCompleted;

    /// <inheritdoc/>
    public event EventHandler<InputWindowTimedOutEventArgs>? WindowTimedOut;

    /// <inheritdoc/>
    public async Task<IInputWindow> CreateAsync(
        InputWindowOptions options,
        CancellationToken ct = default)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "InputWindowManager.CreateAsync");
        ObjectDisposedException.ThrowIf(_disposed, this);

        var windowId = options.WindowId ?? GenerateWindowId();
        var timeout = options.Timeout ?? _defaultTimeout;
        var expiresAt = timeout == TimeSpan.MaxValue ? (DateTime?)null : DateTime.UtcNow + timeout;

        // Resolve default value
        var defaultValue = ResolveDefaultValue(options);

        var window = new InputWindowImpl(
            windowId,
            options.TargetEntity,
            options.WindowType,
            expiresAt,
            options.Options,
            defaultValue,
            options.DefaultSource,
            options.PromptText,
            options.EmitSyncOnComplete);

        if (!_windows.TryAdd(windowId, window))
        {
            throw new InvalidOperationException($"Window ID '{windowId}' already exists");
        }

        _logger?.LogDebug(
            "Created input window {WindowId} for entity {EntityId}, type: {WindowType}, timeout: {Timeout}ms",
            windowId,
            options.TargetEntity,
            options.WindowType,
            timeout.TotalMilliseconds);

        // Start timeout timer if applicable
        if (expiresAt.HasValue)
        {
            _ = StartTimeoutTimerAsync(windowId, timeout, ct);
        }

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
        return window;
    }

    /// <inheritdoc/>
    public IInputWindow? GetWindow(string windowId)
    {
        if (string.IsNullOrEmpty(windowId))
        {
            return null;
        }

        return _windows.TryGetValue(windowId, out var window) ? window : null;
    }

    /// <inheritdoc/>
    public async Task<InputSubmitResult> SubmitAsync(
        string windowId,
        Guid entityId,
        object input,
        CancellationToken ct = default)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "InputWindowManager.SubmitAsync");
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(windowId);

        if (!_windows.TryGetValue(windowId, out var window))
        {
            return InputSubmitResult.Reject("Window not found");
        }

        if (window.State != InputWindowState.Waiting)
        {
            return InputSubmitResult.Reject($"Window is not accepting input (state: {window.State})");
        }

        if (window.TargetEntity != entityId)
        {
            return InputSubmitResult.Reject("Entity is not the target of this window");
        }

        // Validate input
        var validationResult = ValidateInput(window, input);
        if (!validationResult.IsValid)
        {
            return InputSubmitResult.Reject(validationResult.ErrorMessage ?? "Invalid input");
        }

        // Accept and adjudicate
        var adjudicatedValue = AdjudicateInput(window, input);
        window.SetSubmitted(input, adjudicatedValue);

        _logger?.LogDebug(
            "Input submitted for window {WindowId} by entity {EntityId}: {Input} -> {Adjudicated}",
            windowId,
            entityId,
            input,
            adjudicatedValue);

        RaiseWindowCompleted(window, wasDefault: false);

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
        return InputSubmitResult.Accept(adjudicatedValue);
    }

    /// <inheritdoc/>
    public void Close(string windowId, bool useDefault = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_windows.TryGetValue(windowId, out var window))
        {
            return;
        }

        if (window.State != InputWindowState.Waiting)
        {
            return;
        }

        if (useDefault && window.DefaultValue != null)
        {
            window.SetTimedOut(window.DefaultValue);
            RaiseWindowCompleted(window, wasDefault: true);
        }
        else
        {
            window.SetClosed();
        }

        _logger?.LogDebug(
            "Closed input window {WindowId}, useDefault: {UseDefault}",
            windowId,
            useDefault);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<IInputWindow> GetWindowsForEntity(Guid entityId)
    {
        return _windows.Values
            .Where(w => w.TargetEntity == entityId && w.State == InputWindowState.Waiting)
            .Cast<IInputWindow>()
            .ToList()
            .AsReadOnly();
    }

    private string GenerateWindowId()
    {
        var id = Interlocked.Increment(ref _windowIdCounter);
        return $"window_{id:D8}";
    }

    private object? ResolveDefaultValue(InputWindowOptions options)
    {
        // If explicit default provided, use it
        if (options.DefaultValue != null)
        {
            return options.DefaultValue;
        }

        // If using behavior defaults and resolver available
        if (options.DefaultSource == DefaultValueSource.Behavior && _behaviorDefaultResolver != null)
        {
            return _behaviorDefaultResolver(options.TargetEntity);
        }

        // For Choice type, use first marked default option
        if (options.Options != null)
        {
            var defaultOption = options.Options.FirstOrDefault(o => o.IsDefault);
            if (defaultOption != null)
            {
                return defaultOption.Value;
            }

            // Fall back to first option
            if (options.Options.Count > 0)
            {
                return options.Options[0].Value;
            }
        }

        return null;
    }

    private static InputValidationResult ValidateInput(InputWindowImpl window, object input)
    {
        // For Choice type, validate against options
        if (window.WindowType == InputWindowType.Choice && window.Options != null)
        {
            var inputStr = input?.ToString();
            if (!window.Options.Any(o => o.Value == inputStr))
            {
                return InputValidationResult.Invalid($"'{inputStr}' is not a valid option");
            }
        }

        // For Confirmation, validate boolean-like
        if (window.WindowType == InputWindowType.Confirmation)
        {
            if (input is not bool && !bool.TryParse(input?.ToString(), out _))
            {
                return InputValidationResult.Invalid("Expected boolean value");
            }
        }

        return InputValidationResult.Valid();
    }

    private static object AdjudicateInput(InputWindowImpl window, object input)
    {
        // For most cases, accept as-is
        // This is where server-side logic could modify input
        // (e.g., speed dice roll, willingness override)
        return input;
    }

    private async Task StartTimeoutTimerAsync(
        string windowId,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "InputWindowManager.StartTimeoutTimerAsync");
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            await Task.Delay(timeout, linkedCts.Token);

            if (_windows.TryGetValue(windowId, out var window) &&
                window.State == InputWindowState.Waiting)
            {
                window.SetTimedOut(window.DefaultValue);
                RaiseWindowTimedOut(window, timeout);
                RaiseWindowCompleted(window, wasDefault: true);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled or disposed
        }
    }

    private void RaiseWindowCompleted(InputWindowImpl window, bool wasDefault)
    {
        var duration = DateTime.UtcNow - window.CreatedAt;

        WindowCompleted?.Invoke(this, new InputWindowCompletedEventArgs
        {
            WindowId = window.WindowId,
            TargetEntity = window.TargetEntity,
            FinalValue = window.FinalValue,
            WasDefault = wasDefault,
            Duration = duration
        });
    }

    private void RaiseWindowTimedOut(InputWindowImpl window, TimeSpan timeout)
    {
        _logger?.LogDebug(
            "Input window {WindowId} timed out after {Timeout}ms, using default: {Default}",
            window.WindowId,
            timeout.TotalMilliseconds,
            window.DefaultValue);

        WindowTimedOut?.Invoke(this, new InputWindowTimedOutEventArgs
        {
            WindowId = window.WindowId,
            TargetEntity = window.TargetEntity,
            DefaultValue = window.DefaultValue,
            Timeout = timeout
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();

        foreach (var window in _windows.Values)
        {
            window.Dispose();
        }

        _windows.Clear();
    }
}

/// <summary>
/// Internal implementation of an input window.
/// </summary>
internal sealed class InputWindowImpl : IInputWindow, IDisposable
{
    private readonly TaskCompletionSource<object?> _completionTcs;
    private readonly object _lock = new();
    private bool _disposed;

    public InputWindowImpl(
        string windowId,
        Guid targetEntity,
        InputWindowType windowType,
        DateTime? expiresAt,
        IReadOnlyList<InputOption>? options,
        object? defaultValue,
        DefaultValueSource defaultSource,
        string? promptText,
        string? emitSyncOnComplete)
    {
        WindowId = windowId;
        TargetEntity = targetEntity;
        WindowType = windowType;
        ExpiresAt = expiresAt;
        Options = options;
        DefaultValue = defaultValue;
        DefaultSource = defaultSource;
        PromptText = promptText;
        EmitSyncOnComplete = emitSyncOnComplete;
        CreatedAt = DateTime.UtcNow;
        State = InputWindowState.Waiting;
        _completionTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public string WindowId { get; }
    public Guid TargetEntity { get; }
    public InputWindowType WindowType { get; }
    public InputWindowState State { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime? ExpiresAt { get; }
    public IReadOnlyList<InputOption>? Options { get; }
    public object? DefaultValue { get; }
    public DefaultValueSource DefaultSource { get; }
    public string? PromptText { get; }
    public string? EmitSyncOnComplete { get; }
    public object? SubmittedInput { get; private set; }
    public object? FinalValue { get; private set; }

    public TimeSpan? TimeRemaining
    {
        get
        {
            if (ExpiresAt == null)
            {
                return null;
            }

            var remaining = ExpiresAt.Value - DateTime.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public bool HasInput => SubmittedInput != null || State == InputWindowState.TimedOut;

    public bool IsCompleted => State != InputWindowState.Waiting;

    public void SetSubmitted(object input, object adjudicatedValue)
    {
        lock (_lock)
        {
            if (State != InputWindowState.Waiting)
            {
                return;
            }

            SubmittedInput = input;
            FinalValue = adjudicatedValue;
            State = InputWindowState.Submitted;
            _completionTcs.TrySetResult(adjudicatedValue);
        }
    }

    public void SetTimedOut(object? defaultValue)
    {
        lock (_lock)
        {
            if (State != InputWindowState.Waiting)
            {
                return;
            }

            FinalValue = defaultValue;
            State = InputWindowState.TimedOut;
            _completionTcs.TrySetResult(defaultValue);
        }
    }

    public void SetClosed()
    {
        lock (_lock)
        {
            if (State != InputWindowState.Waiting)
            {
                return;
            }

            State = InputWindowState.Closed;
            _completionTcs.TrySetCanceled();
        }
    }

    public async Task<object?> WaitForCompletionAsync(CancellationToken ct = default)
    {
        return await _completionTcs.Task.WaitAsync(ct);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _completionTcs.TrySetCanceled();
    }
}

/// <summary>
/// Result of input validation.
/// </summary>
internal readonly record struct InputValidationResult(bool IsValid, string? ErrorMessage)
{
    public static InputValidationResult Valid() => new(true, null);
    public static InputValidationResult Invalid(string message) => new(false, message);
}
