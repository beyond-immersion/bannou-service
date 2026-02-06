// ═══════════════════════════════════════════════════════════════════════════
// ABML Runtime Exception
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Exceptions;

namespace BeyondImmersion.BannouService.Abml.Exceptions;

/// <summary>
/// Exception thrown when an error occurs during ABML expression execution.
/// </summary>
public class AbmlRuntimeException : AbmlException
{
    /// <summary>Gets the instruction index where the error occurred.</summary>
    public int? InstructionIndex { get; }

    /// <summary>Creates a new runtime exception.</summary>
    public AbmlRuntimeException(string message) : base(message) { }

    /// <summary>Creates a new runtime exception with inner exception.</summary>
    public AbmlRuntimeException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Creates a new runtime exception with source context.</summary>
    public AbmlRuntimeException(string message, string sourceExpression, int? instructionIndex = null)
        : base(message, sourceExpression, null) => InstructionIndex = instructionIndex;

    /// <summary>Creates a new runtime exception with inner exception and source context.</summary>
    public AbmlRuntimeException(string message, Exception innerException, string sourceExpression, int? instructionIndex = null)
        : base(message, innerException, sourceExpression, null) => InstructionIndex = instructionIndex;

    /// <summary>Creates a type error exception.</summary>
    public static AbmlRuntimeException TypeError(string op, string expected, string actual, string? source = null) =>
        new($"Type error in {op}: expected {expected}, got {actual}", source ?? "", null);

    /// <summary>Creates an invalid operation exception.</summary>
    public static AbmlRuntimeException InvalidOperation(string op, string reason, string? source = null) =>
        new($"Invalid operation '{op}': {reason}", source ?? "", null);

    /// <summary>Creates a function error exception.</summary>
    public static AbmlRuntimeException FunctionError(string funcName, string reason, string? source = null) =>
        new($"Error calling function '{funcName}': {reason}", source ?? "", null);
}
