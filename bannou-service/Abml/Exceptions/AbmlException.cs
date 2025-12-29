// ═══════════════════════════════════════════════════════════════════════════
// ABML Base Exception
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Abml.Exceptions;

/// <summary>
/// Base exception for all ABML-related errors.
/// </summary>
public class AbmlException : Exception
{
    /// <summary>Gets the source expression that caused the exception.</summary>
    public string? SourceExpression { get; }

    /// <summary>Gets the position in the source where the error occurred.</summary>
    public int? Position { get; }

    /// <summary>Creates a new ABML exception.</summary>
    public AbmlException(string message) : base(message) { }

    /// <summary>Creates a new ABML exception with inner exception.</summary>
    public AbmlException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Creates a new ABML exception with source context.</summary>
    public AbmlException(string message, string? sourceExpression, int? position = null)
        : base(FormatMessage(message, sourceExpression, position))
    {
        SourceExpression = sourceExpression;
        Position = position;
    }

    /// <summary>Creates a new ABML exception with inner exception and source context.</summary>
    public AbmlException(string message, Exception innerException, string? sourceExpression, int? position = null)
        : base(FormatMessage(message, sourceExpression, position), innerException)
    {
        SourceExpression = sourceExpression;
        Position = position;
    }

    private static string FormatMessage(string message, string? source, int? position)
    {
        if (string.IsNullOrEmpty(source)) return message;
        if (position.HasValue) return $"{message} at position {position.Value} in: {source}";
        return $"{message} in: {source}";
    }
}
