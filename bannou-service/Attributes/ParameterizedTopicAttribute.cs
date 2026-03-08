namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Marks a generated event publisher extension method as having a parameterized topic.
/// The topic string contains placeholders (e.g., <c>asset.processing.job.{poolType}</c>)
/// that must be resolved at publish time. The generated overload accepts these as typed
/// parameters and uses string interpolation to produce the concrete topic.
/// </summary>
/// <remarks>
/// <para>
/// <b>SCHEMA-FIRST:</b> This attribute is generated from <c>topic-params</c> in the
/// service's <c>x-event-publications</c> schema declaration.
/// </para>
/// <para>
/// The <see cref="ParameterTypes"/> array lists the C# types of each topic parameter
/// in declaration order. The structural test uses this to validate that the service
/// calls either the parameterized overload or the base (template-string) overload.
/// </para>
/// </remarks>
[AttributeUsage(validOn: AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ParameterizedTopicAttribute : Attribute
{
    /// <summary>
    /// The C# types of the topic parameters in declaration order.
    /// </summary>
    public Type[] ParameterTypes { get; }

    /// <summary>
    /// Initializes a new instance with the specified parameter types.
    /// </summary>
    /// <param name="parameterTypes">The C# types of each topic parameter in order.</param>
    public ParameterizedTopicAttribute(params Type[] parameterTypes)
    {
        ParameterTypes = parameterTypes;
    }
}
