namespace BeyondImmersion.BannouService.Tools.Inspect.Models;

/// <summary>
/// Represents inspected type information.
/// </summary>
public record TypeInfo
{
    /// <summary>Gets or sets the full type name.</summary>
    public required string FullName { get; init; }

    /// <summary>Gets or sets the type kind (class, interface, struct, enum, delegate).</summary>
    public required string Kind { get; init; }

    /// <summary>Gets or sets the base type if any.</summary>
    public string? BaseType { get; init; }

    /// <summary>Gets or sets the implemented interfaces.</summary>
    public required IReadOnlyList<string> Interfaces { get; init; }

    /// <summary>Gets or sets the XML documentation summary.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets or sets the XML documentation remarks.</summary>
    public string? Remarks { get; init; }

    /// <summary>Gets or sets the public constructors.</summary>
    public required IReadOnlyList<ConstructorInfo> Constructors { get; init; }

    /// <summary>Gets or sets the public methods.</summary>
    public required IReadOnlyList<MethodInfo> Methods { get; init; }

    /// <summary>Gets or sets the public properties.</summary>
    public required IReadOnlyList<PropertyInfo> Properties { get; init; }

    /// <summary>Gets or sets the public events.</summary>
    public required IReadOnlyList<EventInfo> Events { get; init; }

    /// <summary>Gets or sets the generic type parameters.</summary>
    public required IReadOnlyList<string> GenericParameters { get; init; }

    /// <summary>Gets or sets the source assembly name.</summary>
    public required string AssemblyName { get; init; }
}

/// <summary>
/// Represents inspected constructor information.
/// </summary>
public record ConstructorInfo
{
    /// <summary>Gets or sets the declaring type name.</summary>
    public required string TypeName { get; init; }

    /// <summary>Gets or sets the constructor parameters.</summary>
    public required IReadOnlyList<ParameterInfo> Parameters { get; init; }

    /// <summary>Gets or sets the XML documentation summary.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets or sets the documented exceptions.</summary>
    public required IReadOnlyList<ExceptionInfo> Exceptions { get; init; }

    /// <summary>Gets or sets whether this is a static constructor.</summary>
    public bool IsStatic { get; init; }
}

/// <summary>
/// Represents inspected method information.
/// </summary>
public record MethodInfo
{
    /// <summary>Gets or sets the method name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets or sets the return type.</summary>
    public required string ReturnType { get; init; }

    /// <summary>Gets or sets the method parameters.</summary>
    public required IReadOnlyList<ParameterInfo> Parameters { get; init; }

    /// <summary>Gets or sets the XML documentation summary.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets or sets the XML documentation for the return value.</summary>
    public string? Returns { get; init; }

    /// <summary>Gets or sets the documented exceptions.</summary>
    public required IReadOnlyList<ExceptionInfo> Exceptions { get; init; }

    /// <summary>Gets or sets the generic type parameters.</summary>
    public required IReadOnlyList<string> GenericParameters { get; init; }

    /// <summary>Gets or sets whether the method is static.</summary>
    public bool IsStatic { get; init; }

    /// <summary>Gets or sets whether the method is async.</summary>
    public bool IsAsync { get; init; }
}

/// <summary>
/// Represents inspected property information.
/// </summary>
public record PropertyInfo
{
    /// <summary>Gets or sets the property name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets or sets the property type.</summary>
    public required string Type { get; init; }

    /// <summary>Gets or sets whether the property has a getter.</summary>
    public bool HasGetter { get; init; }

    /// <summary>Gets or sets whether the property has a setter.</summary>
    public bool HasSetter { get; init; }

    /// <summary>Gets or sets the XML documentation summary.</summary>
    public string? Summary { get; init; }
}

/// <summary>
/// Represents inspected event information.
/// </summary>
public record EventInfo
{
    /// <summary>Gets or sets the event name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets or sets the event handler type.</summary>
    public required string Type { get; init; }

    /// <summary>Gets or sets the XML documentation summary.</summary>
    public string? Summary { get; init; }
}

/// <summary>
/// Represents inspected parameter information.
/// </summary>
public record ParameterInfo
{
    /// <summary>Gets or sets the parameter name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets or sets the parameter type.</summary>
    public required string Type { get; init; }

    /// <summary>Gets or sets whether the parameter is optional.</summary>
    public bool IsOptional { get; init; }

    /// <summary>Gets or sets the default value if optional.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Gets or sets the XML documentation.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Represents documented exception information.
/// </summary>
public record ExceptionInfo
{
    /// <summary>Gets or sets the exception type.</summary>
    public required string Type { get; init; }

    /// <summary>Gets or sets the exception description.</summary>
    public string? Description { get; init; }
}
