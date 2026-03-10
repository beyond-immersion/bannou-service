// =============================================================================
// Evaluation Result
// Allocation-free result type for behavior model evaluation with pause support.
// =============================================================================

namespace BeyondImmersion.Bannou.Client.Behavior.Runtime;

/// <summary>
/// Result of a behavior model evaluation. Allocation-free readonly struct.
/// </summary>
/// <remarks>
/// <para>
/// When evaluation completes normally, use <see cref="Completed"/>.
/// When evaluation pauses at a continuation point, the result contains
/// the continuation point index and timeout for extension injection.
/// </para>
/// </remarks>
public readonly struct EvaluationResult
{
    /// <summary>
    /// Evaluation completed normally without pause.
    /// </summary>
    public static readonly EvaluationResult Completed = new(EvaluationStatus.Completed, -1, 0);

    /// <summary>
    /// Status of the evaluation.
    /// </summary>
    public EvaluationStatus Status { get; }

    /// <summary>
    /// Index of continuation point if paused (-1 if not paused).
    /// </summary>
    public int ContinuationPointIndex { get; }

    /// <summary>
    /// Timeout in milliseconds if paused (0 if not paused or no timeout).
    /// </summary>
    public uint TimeoutMs { get; }

    /// <summary>
    /// Whether evaluation is paused at a continuation point.
    /// </summary>
    public bool IsPaused => Status == EvaluationStatus.PausedAtContinuationPoint;

    /// <summary>
    /// Creates a new evaluation result.
    /// </summary>
    /// <param name="status">The evaluation status.</param>
    /// <param name="cpIndex">Continuation point index (-1 if not paused).</param>
    /// <param name="timeoutMs">Timeout in milliseconds (0 if no timeout).</param>
    public EvaluationResult(EvaluationStatus status, int cpIndex, uint timeoutMs)
    {
        Status = status;
        ContinuationPointIndex = cpIndex;
        TimeoutMs = timeoutMs;
    }

    /// <summary>
    /// Creates a result indicating pause at a continuation point.
    /// </summary>
    /// <param name="cpIndex">The continuation point index.</param>
    /// <param name="timeoutMs">The timeout in milliseconds.</param>
    /// <returns>An evaluation result indicating pause.</returns>
    public static EvaluationResult PausedAt(int cpIndex, uint timeoutMs)
    {
        return new EvaluationResult(EvaluationStatus.PausedAtContinuationPoint, cpIndex, timeoutMs);
    }
}

/// <summary>
/// Status of a behavior model evaluation.
/// </summary>
public enum EvaluationStatus : byte
{
    /// <summary>
    /// Evaluation completed normally (reached end or Halt opcode).
    /// </summary>
    Completed = 0,

    /// <summary>
    /// Evaluation paused at a continuation point waiting for extension.
    /// </summary>
    PausedAtContinuationPoint = 1,

    /// <summary>
    /// An extension model is currently executing.
    /// </summary>
    ExtensionExecuting = 2
}
