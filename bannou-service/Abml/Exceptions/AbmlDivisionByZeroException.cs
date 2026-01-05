// ═══════════════════════════════════════════════════════════════════════════
// ABML Division By Zero Exception
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Abml.Exceptions;

/// <summary>
/// Exception thrown when division by zero occurs.
/// </summary>
public class AbmlDivisionByZeroException : AbmlRuntimeException
{
    /// <summary>Gets the operation type.</summary>
    public DivisionOperation Operation { get; }

    /// <summary>Creates a new division by zero exception.</summary>
    public AbmlDivisionByZeroException() : base("Division by zero") => Operation = DivisionOperation.Division;

    /// <summary>Creates a new division by zero exception for a specific operation.</summary>
    public AbmlDivisionByZeroException(DivisionOperation operation)
        : base(operation == DivisionOperation.Modulo ? "Modulo by zero" : "Division by zero")
        => Operation = operation;

    /// <summary>Creates a new division by zero exception with source context.</summary>
    public AbmlDivisionByZeroException(DivisionOperation operation, string sourceExpression)
        : base(operation == DivisionOperation.Modulo ? "Modulo by zero" : "Division by zero", sourceExpression, null)
        => Operation = operation;

    /// <summary>Creates a new division by zero exception with instruction index.</summary>
    public AbmlDivisionByZeroException(DivisionOperation operation, string sourceExpression, int instructionIndex)
        : base(operation == DivisionOperation.Modulo ? "Modulo by zero" : "Division by zero", sourceExpression, instructionIndex)
        => Operation = operation;

    /// <summary>Creates a division exception.</summary>
    public static AbmlDivisionByZeroException Division(string? source = null) =>
        source is null ? new(DivisionOperation.Division) : new(DivisionOperation.Division, source);

    /// <summary>Creates a modulo exception.</summary>
    public static AbmlDivisionByZeroException Modulo(string? source = null) =>
        source is null ? new(DivisionOperation.Modulo) : new(DivisionOperation.Modulo, source);
}

/// <summary>
/// Type of division operation.
/// </summary>
public enum DivisionOperation
{ /// <inheritdoc/>
    Division, /// <inheritdoc/>
    Modulo
}
