// ═══════════════════════════════════════════════════════════════════════════
// ABML Compilation Exception
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Abml.Exceptions;

/// <summary>
/// Exception thrown when an ABML expression fails to parse or compile.
/// </summary>
public class AbmlCompilationException : AbmlException
{
    /// <summary>Gets the line number where the error occurred.</summary>
    public int? Line { get; }

    /// <summary>Gets the column number where the error occurred.</summary>
    public int? Column { get; }

    /// <summary>Creates a new compilation exception.</summary>
    public AbmlCompilationException(string message) : base(message) { }

    /// <summary>Creates a new compilation exception with inner exception.</summary>
    public AbmlCompilationException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Creates a new compilation exception with source context.</summary>
    public AbmlCompilationException(string message, string sourceExpression, int? position = null)
        : base(message, sourceExpression, position) { }

    /// <summary>Creates an exception for an unexpected token.</summary>
    public static AbmlCompilationException UnexpectedToken(string expected, string actual, string source, int pos) =>
        new($"Expected '{expected}' but found '{actual}'", source, pos);

    /// <summary>Creates an exception for unexpected end of expression.</summary>
    public static AbmlCompilationException UnexpectedEndOfExpression(string source) =>
        new("Unexpected end of expression", source, source.Length);

    /// <summary>Creates an exception for invalid syntax.</summary>
    public static AbmlCompilationException InvalidSyntax(string description, string source, int? pos = null) =>
        new($"Invalid syntax: {description}", source, pos);
}
