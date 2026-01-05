// ═══════════════════════════════════════════════════════════════════════════
// ABML Parse Result Types
// Result types for document parsing operations.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Abml.Parser;

/// <summary>
/// Result of a parse operation.
/// </summary>
/// <typeparam name="T">The type of the parsed value.</typeparam>
public sealed class ParseResult<T>
{
    /// <summary>
    /// Whether parsing succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// The parsed value (if successful).
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Parse errors (if unsuccessful).
    /// </summary>
    public IReadOnlyList<ParseError> Errors { get; }

    private ParseResult(bool isSuccess, T? value, IReadOnlyList<ParseError> errors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Errors = errors;
    }

    /// <summary>
    /// Creates a successful parse result.
    /// </summary>
    public static ParseResult<T> Success(T value) =>
        new(true, value, []);

    /// <summary>
    /// Creates a failed parse result.
    /// </summary>
    public static ParseResult<T> Failure(IReadOnlyList<ParseError> errors) =>
        new(false, default, errors);

    /// <summary>
    /// Creates a failed parse result with a single error.
    /// </summary>
    public static ParseResult<T> Failure(ParseError error) =>
        new(false, default, [error]);

    /// <summary>
    /// Creates a failed parse result with a message.
    /// </summary>
    public static ParseResult<T> Failure(string message, int line = 0, int column = 0) =>
        new(false, default, [new ParseError(message, line, column)]);
}

/// <summary>
/// Represents a parse error with location information.
/// </summary>
/// <param name="Message">Error message.</param>
/// <param name="Line">Line number (1-based).</param>
/// <param name="Column">Column number (1-based).</param>
public sealed record ParseError(string Message, int Line = 0, int Column = 0)
{
    /// <summary>
    /// Returns a formatted error message with location.
    /// </summary>
    public override string ToString() =>
        Line > 0
            ? Column > 0
                ? $"Line {Line}, Column {Column}: {Message}"
                : $"Line {Line}: {Message}"
            : Message;
}
