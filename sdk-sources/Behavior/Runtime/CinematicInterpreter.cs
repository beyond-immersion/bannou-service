// =============================================================================
// Cinematic Interpreter
// Handles streaming composition and continuation points for behavior models.
// =============================================================================

namespace BeyondImmersion.Bannou.Client.SDK.Behavior.Runtime;

/// <summary>
/// Interpreter for cinematic composition using continuation points.
/// Manages streaming extensions and base model coordination.
/// </summary>
public sealed class CinematicInterpreter
{
    private readonly BehaviorModel _baseModel;
    private readonly BehaviorModelInterpreter _interpreter;
    private readonly Dictionary<string, ExtensionModel> _extensions = new(StringComparer.Ordinal);
    private readonly double[] _inputState;
    private readonly double[] _outputState;

    private ContinuationState? _activeContinuation;
    private DateTime _continuationStartTime;

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
    /// Whether an extension is currently active at a continuation point.
    /// </summary>
    public bool HasActiveExtension => _activeContinuation != null;

    /// <summary>
    /// The name of the currently active continuation point, if any.
    /// </summary>
    public string? ActiveContinuationPoint => _activeContinuation?.PointName;

    /// <summary>
    /// The input state array for external modification.
    /// </summary>
    public double[] InputState => _inputState;

    /// <summary>
    /// The output state array for reading results.
    /// </summary>
    public ReadOnlySpan<double> OutputState => _outputState;

    /// <summary>
    /// Registers an extension model for a continuation point.
    /// </summary>
    /// <param name="continuationPointName">The continuation point name.</param>
    /// <param name="extensionModel">The extension model.</param>
    public void RegisterExtension(string continuationPointName, BehaviorModel extensionModel)
    {
        ArgumentNullException.ThrowIfNull(continuationPointName);
        ArgumentNullException.ThrowIfNull(extensionModel);

        _extensions[continuationPointName] = new ExtensionModel(extensionModel, new BehaviorModelInterpreter(extensionModel));
    }

    /// <summary>
    /// Removes an extension from a continuation point.
    /// </summary>
    /// <param name="continuationPointName">The continuation point name.</param>
    /// <returns>True if the extension was removed.</returns>
    public bool RemoveExtension(string continuationPointName)
    {
        if (_activeContinuation?.PointName == continuationPointName)
        {
            _activeContinuation = null;
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
    /// Evaluates the current state, handling continuation points.
    /// </summary>
    /// <returns>The evaluation result.</returns>
    public CinematicEvaluationResult Evaluate()
    {
        // Check for continuation timeout
        if (_activeContinuation != null)
        {
            var elapsed = DateTime.UtcNow - _continuationStartTime;
            if (elapsed.TotalMilliseconds >= _activeContinuation.Value.TimeoutMs)
            {
                // Timeout - deactivate continuation and resume default flow
                _activeContinuation = null;
            }
        }

        // If we have an active continuation with an extension, evaluate it
        if (_activeContinuation != null && _extensions.TryGetValue(_activeContinuation.Value.PointName, out var ext))
        {
            ext.Interpreter.Evaluate(_inputState, _outputState);
            return new CinematicEvaluationResult(true, _activeContinuation.Value.PointName);
        }

        // Evaluate base model
        _interpreter.Evaluate(_inputState, _outputState);

        // Check if we hit a continuation point by examining the state
        // The basic interpreter handles continuation points by jumping to default flow
        // For the cinematic interpreter, we need to intercept before that happens
        // This is a simplified version - a full implementation would need custom bytecode interpretation

        return new CinematicEvaluationResult(false, null);
    }

    /// <summary>
    /// Signals that an extension has completed.
    /// </summary>
    public void CompleteExtension()
    {
        _activeContinuation = null;
    }

    /// <summary>
    /// Activates a continuation point for streaming extension.
    /// Call this when you want to pause at a continuation point and wait for an extension.
    /// </summary>
    /// <param name="continuationPointName">The continuation point name.</param>
    /// <param name="timeoutMs">Optional timeout override in milliseconds.</param>
    public void ActivateContinuationPoint(string continuationPointName, uint? timeoutMs = null)
    {
        if (_baseModel.ContinuationPoints.TryGetByName(continuationPointName, out var cp))
        {
            _activeContinuation = new ContinuationState(continuationPointName, timeoutMs ?? cp.TimeoutMs);
            _continuationStartTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Resets the interpreter state.
    /// </summary>
    public void Reset()
    {
        _activeContinuation = null;
        InitializeDefaultState();
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

    private readonly record struct ContinuationState(string PointName, uint TimeoutMs);
}

/// <summary>
/// Result of a cinematic evaluation.
/// </summary>
public readonly record struct CinematicEvaluationResult(bool IsAtContinuationPoint, string? ContinuationPointName);
