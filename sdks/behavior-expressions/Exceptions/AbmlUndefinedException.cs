// ═══════════════════════════════════════════════════════════════════════════
// ABML Undefined Exception
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.BehaviorExpressions.Exceptions;

/// <summary>
/// Exception thrown when accessing an undefined variable or property.
/// </summary>
public class AbmlUndefinedException : AbmlRuntimeException
{
    /// <summary>Gets the undefined name.</summary>
    public string UndefinedName { get; }

    /// <summary>Gets the type of undefined reference.</summary>
    public UndefinedReferenceType ReferenceType { get; }

    /// <summary>Creates a new undefined exception for a variable.</summary>
    public AbmlUndefinedException(string variableName)
        : base($"Undefined variable: '{variableName}'")
    {
        UndefinedName = variableName;
        ReferenceType = UndefinedReferenceType.Variable;
    }

    /// <summary>Creates a new undefined exception with reference type.</summary>
    public AbmlUndefinedException(string name, UndefinedReferenceType referenceType)
        : base($"Undefined {referenceType.ToString().ToLowerInvariant()}: '{name}'")
    {
        UndefinedName = name;
        ReferenceType = referenceType;
    }

    /// <summary>Creates an exception for an undefined variable.</summary>
    public static AbmlUndefinedException Variable(string name, string? source = null) =>
        new(name, UndefinedReferenceType.Variable);

    /// <summary>Creates an exception for an undefined property.</summary>
    public static AbmlUndefinedException Property(string name, string targetType, string? source = null) =>
        new(name, UndefinedReferenceType.Property);

    /// <summary>Creates an exception for an undefined function.</summary>
    public static AbmlUndefinedException Function(string name, string? source = null) =>
        new(name, UndefinedReferenceType.Function);
}

/// <summary>
/// Type of undefined reference.
/// </summary>
public enum UndefinedReferenceType
{
    /// <summary>Variable reference.</summary>
    Variable,

    /// <summary>Property reference.</summary>
    Property,

    /// <summary>Function reference.</summary>
    Function,

    /// <summary>Index reference.</summary>
    Index
}
