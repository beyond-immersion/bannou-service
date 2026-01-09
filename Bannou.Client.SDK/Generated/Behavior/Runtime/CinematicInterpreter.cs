// =============================================================================
// Cinematic Interpreter
// Handles streaming composition and continuation points for behavior models.
// =============================================================================

namespace BeyondImmersion.Bannou.Client.SDK.Behavior.Runtime;

/// <summary>
/// Interpreter for cinematic composition using continuation points.
/// Manages streaming extensions and base model coordination.
/// </summary>
/// <remarks>
/// <para>
/// The CinematicInterpreter wraps a base behavior model and intercepts execution
/// at continuation points. When the base model reaches a continuation point,
/// execution pauses to allow extension injection.
/// </para>
/// <para>
/// Extensions can be pre-registered or injected dynamically while waiting.
/// If no extension arrives within the timeout, the default flow is executed.
/// </para>
/// </remarks>
public sealed class CinematicInterpreter
{
    private readonly BehaviorModel _baseModel;
    private readonly BehaviorModelInterpreter _interpreter;
    private readonly Dictionary<string, ExtensionModel> _extensions = new(StringComparer.Ordinal);
    private readonly double[] _inputState;
    private readonly double[] _outputState;

    // Waiting state for continuation points
    private bool _waitingForExtension;
    private string? _waitingCpName;
    private uint _waitingCpTimeout;
    private DateTime _waitingStartTime;

    // Pending extension (injected while waiting)
    private ExtensionModel? _pendingExtension;

    /// <summary>
    /// Creates a new cinematic interpreter for a base model.
    /// </summary>
    /// <param name="baseModel">The base behavior model.</param>
    public CinematicInterpreter(BehaviorModel baseModel)
    {
        ArgumentNullException.ThrowIfNull(baseModel);

        _baseModel = baseModel;
        _interpreter = new BehaviorModelInterpreter(baseModel);
        _inputState = new double[baseModel.Schema.InputCount];
        _outputState = new double[baseModel.Schema.OutputCount];

        // Initialize with default values
        InitializeDefaultState();
    }

    /// <summary>
    /// Whether execution is waiting at a continuation point.
    /// </summary>
    public bool IsWaitingForExtension => _waitingForExtension;

    /// <summary>
    /// The name of the continuation point currently waiting, if any.
    /// </summary>
    public string? WaitingContinuationPoint => _waitingCpName;

    /// <summary>
    /// The input state array for external modification.
    /// </summary>
    public double[] InputState => _inputState;

    /// <summary>
    /// The output state array for reading results.
    /// </summary>
    public ReadOnlySpan<double> OutputState => _outputState;

    /// <summary>
    /// Pre-registers an extension model for a continuation point.
    /// </summary>
    /// <remarks>
    /// Extensions can be registered before or during execution.
    /// If registered while waiting at the matching continuation point,
    /// the extension will be used in the next Evaluate call.
    /// </remarks>
    /// <param name="continuationPointName">The continuation point name.</param>
    /// <param name="extensionModel">The extension model.</param>
    public void RegisterExtension(string continuationPointName, BehaviorModel extensionModel)
    {
        ArgumentNullException.ThrowIfNull(continuationPointName);
        ArgumentNullException.ThrowIfNull(extensionModel);

        var ext = new ExtensionModel(extensionModel, new BehaviorModelInterpreter(extensionModel));
        _extensions[continuationPointName] = ext;

        // If we're currently waiting for this exact continuation point, set as pending
        if (_waitingForExtension && _waitingCpName == continuationPointName)
        {
            _pendingExtension = ext;
        }
    }

    /// <summary>
    /// Injects an extension that arrived via streaming.
    /// </summary>
    /// <remarks>
    /// Use this method when an extension arrives from the network while
    /// the interpreter is waiting at a continuation point.
    /// </remarks>
    /// <param name="continuationPointName">The continuation point name.</param>
    /// <param name="extensionModel">The extension model.</param>
    /// <returns>True if the extension was accepted (waiting at the matching point).</returns>
    public bool InjectExtension(string continuationPointName, BehaviorModel extensionModel)
    {
        ArgumentNullException.ThrowIfNull(continuationPointName);
        ArgumentNullException.ThrowIfNull(extensionModel);

        if (_waitingForExtension && _waitingCpName == continuationPointName)
        {
            _pendingExtension = new ExtensionModel(extensionModel, new BehaviorModelInterpreter(extensionModel));
            return true;
        }

        // Not waiting at this point, but register for future use
        RegisterExtension(continuationPointName, extensionModel);
        return false;
    }

    /// <summary>
    /// Removes an extension from a continuation point.
    /// </summary>
    /// <param name="continuationPointName">The continuation point name.</param>
    /// <returns>True if the extension was removed.</returns>
    public bool RemoveExtension(string continuationPointName)
    {
        if (_waitingCpName == continuationPointName)
        {
            _pendingExtension = null;
        }
        return _extensions.Remove(continuationPointName);
    }

    /// <summary>
    /// Sets an input value by name.
    /// </summary>
    /// <param name="name">The input variable name.</param>
    /// <param name="value">The value to set.</param>
    public void SetInput(string name, double value)
    {
        var hash = ComputeNameHash(name);
        if (_baseModel.Schema.TryGetInputIndex(hash, out var index))
        {
            _inputState[index] = value;
        }
    }

    /// <summary>
    /// Gets an output value by name.
    /// </summary>
    /// <param name="name">The output variable name.</param>
    /// <returns>The output value, or 0 if not found.</returns>
    public double GetOutput(string name)
    {
        var hash = ComputeNameHash(name);
        if (_baseModel.Schema.TryGetOutputIndex(hash, out var index))
        {
            return _outputState[index];
        }
        return 0.0;
    }

    /// <summary>
    /// Evaluates the current state, handling continuation points with pause support.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method uses the new EvaluateWithPause mechanism to properly intercept
    /// continuation points at the bytecode level. When a continuation point is reached:
    /// </para>
    /// <list type="number">
    /// <item>If a pre-registered extension exists, it executes immediately</item>
    /// <item>Otherwise, enters waiting state and returns "waiting" result</item>
    /// <item>On subsequent calls while waiting, checks for timeout or pending extension</item>
    /// </list>
    /// </remarks>
    /// <returns>The evaluation result.</returns>
    public CinematicEvaluationResult Evaluate()
    {
        // If waiting for extension, handle timeout and pending extensions
        if (_waitingForExtension)
        {
            // Check for timeout
            var elapsed = DateTime.UtcNow - _waitingStartTime;
            if (elapsed.TotalMilliseconds >= _waitingCpTimeout)
            {
                // Timeout - resume with default flow
                _waitingForExtension = false;
                var cpName = _waitingCpName;
                _waitingCpName = null;
                _pendingExtension = null;

                var resumeResult = _interpreter.ResumeWithDefaultFlow(_inputState, _outputState);

                // Continue evaluating if resumed successfully
                if (resumeResult.IsPaused)
                {
                    return HandlePausedResult(resumeResult);
                }

                return new CinematicEvaluationResult(
                    CinematicStatus.Completed,
                    null,
                    $"Timeout at {cpName}, used default flow");
            }

            // Check for pending extension
            if (_pendingExtension != null)
            {
                var ext = _pendingExtension.Value;
                _pendingExtension = null;
                _waitingForExtension = false;
                var cpName = _waitingCpName;
                _waitingCpName = null;

                // Execute extension
                _interpreter.ResumeWithExtension(ext.Interpreter, _inputState, _outputState);

                return new CinematicEvaluationResult(
                    CinematicStatus.ExtensionExecuted,
                    cpName,
                    "Extension injected and executed");
            }

            // Still waiting
            return new CinematicEvaluationResult(
                CinematicStatus.WaitingForExtension,
                _waitingCpName,
                null);
        }

        // Normal evaluation with pause support
        var result = _interpreter.EvaluateWithPause(_inputState, _outputState);

        if (result.IsPaused)
        {
            return HandlePausedResult(result);
        }

        return new CinematicEvaluationResult(CinematicStatus.Completed, null, null);
    }

    /// <summary>
    /// Forces resumption with the default flow, even before timeout.
    /// </summary>
    /// <remarks>
    /// Use this when you know no extension will arrive and want to continue immediately.
    /// </remarks>
    /// <returns>True if resumed, false if not waiting.</returns>
    public bool ForceResumeWithDefaultFlow()
    {
        if (!_waitingForExtension)
        {
            return false;
        }

        _waitingForExtension = false;
        _waitingCpName = null;
        _pendingExtension = null;

        _interpreter.ResumeWithDefaultFlow(_inputState, _outputState);
        return true;
    }

    /// <summary>
    /// Resets the interpreter state.
    /// </summary>
    public void Reset()
    {
        _waitingForExtension = false;
        _waitingCpName = null;
        _pendingExtension = null;
        _interpreter.ClearPauseState();
        InitializeDefaultState();
    }

    private CinematicEvaluationResult HandlePausedResult(EvaluationResult result)
    {
        var cpIndex = result.ContinuationPointIndex;
        var cpName = GetContinuationPointName(_baseModel.ContinuationPoints[cpIndex]);

        // Check if extension is pre-registered
        if (_extensions.TryGetValue(cpName, out var ext))
        {
            // Extension available - execute immediately
            _interpreter.ResumeWithExtension(ext.Interpreter, _inputState, _outputState);

            return new CinematicEvaluationResult(
                CinematicStatus.ExtensionExecuted,
                cpName,
                "Pre-registered extension executed");
        }

        // No extension - start waiting
        _waitingForExtension = true;
        _waitingCpName = cpName;
        _waitingCpTimeout = result.TimeoutMs;
        _waitingStartTime = DateTime.UtcNow;

        // If timeout is 0, immediately use default flow
        if (result.TimeoutMs == 0)
        {
            _waitingForExtension = false;
            _waitingCpName = null;
            _interpreter.ResumeWithDefaultFlow(_inputState, _outputState);

            return new CinematicEvaluationResult(
                CinematicStatus.Completed,
                cpName,
                "No-wait continuation point, used default flow");
        }

        return new CinematicEvaluationResult(
            CinematicStatus.WaitingForExtension,
            cpName,
            null);
    }

    private void InitializeDefaultState()
    {
        // Initialize inputs with default values
        for (var i = 0; i < _baseModel.Schema.Inputs.Count; i++)
        {
            _inputState[i] = _baseModel.Schema.Inputs[i].DefaultValue;
        }

        // Initialize outputs with default values
        for (var i = 0; i < _baseModel.Schema.Outputs.Count; i++)
        {
            _outputState[i] = _baseModel.Schema.Outputs[i].DefaultValue;
        }
    }

    private string GetContinuationPointName(ContinuationPoint cp)
    {
        // Look up name in string table
        if (cp.NameStringIndex >= 0 && cp.NameStringIndex < _baseModel.StringTable.Count)
        {
            return _baseModel.StringTable[cp.NameStringIndex];
        }
        return $"cp_{cp.NameStringIndex}";
    }

    private static uint ComputeNameHash(string name)
    {
        // FNV-1a hash
        var hash = 2166136261u;
        foreach (var c in name)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash;
    }

    private readonly record struct ExtensionModel(BehaviorModel Model, BehaviorModelInterpreter Interpreter);
}

/// <summary>
/// Status of a cinematic evaluation.
/// </summary>
public enum CinematicStatus
{
    /// <summary>Evaluation completed normally.</summary>
    Completed,

    /// <summary>Waiting at a continuation point for an extension.</summary>
    WaitingForExtension,

    /// <summary>An extension was executed at a continuation point.</summary>
    ExtensionExecuted
}

/// <summary>
/// Result of a cinematic evaluation.
/// </summary>
/// <param name="Status">The evaluation status.</param>
/// <param name="ContinuationPointName">The continuation point name, if applicable.</param>
/// <param name="Message">Optional message with details.</param>
public readonly record struct CinematicEvaluationResult(
    CinematicStatus Status,
    string? ContinuationPointName,
    string? Message)
{
    /// <summary>
    /// Whether evaluation is waiting for an extension.
    /// </summary>
    public bool IsWaiting => Status == CinematicStatus.WaitingForExtension;

    /// <summary>
    /// Whether evaluation has completed.
    /// </summary>
    public bool IsCompleted => Status == CinematicStatus.Completed;

    /// <summary>
    /// Whether an extension was executed.
    /// </summary>
    public bool ExtensionWasExecuted => Status == CinematicStatus.ExtensionExecuted;
}
