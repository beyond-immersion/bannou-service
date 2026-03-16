namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Marks a configuration property as a member of a constraint group.
/// Properties sharing the same group name are validated collectively at startup
/// according to the group's constraint type defined by <see cref="ConfigConstraintGroupDefinitionAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is generated from <c>constraint-group: {name}</c> on configuration schema
/// properties and validated at startup by <see cref="Configuration.IServiceConfiguration.ValidateConstraintGroups"/>.
/// </para>
/// <para>
/// A property may belong to at most one constraint group. Groups are scoped to their
/// configuration class — the same group name in different configuration classes (including
/// helper configurations within the same plugin) does not conflict.
/// </para>
/// <para>
/// The group must be defined on the same class via <see cref="ConfigConstraintGroupDefinitionAttribute"/>.
/// </para>
/// </remarks>
/// <example>
/// Schema:
/// <code>
/// GatheringWeight:
///   type: number
///   nullable: true
///   constraint-group: stage-weights
///   env: CRAFT_GATHERING_WEIGHT
///   default: 0.25
/// </code>
/// Generated:
/// <code>
/// [ConfigConstraintGroup("stage-weights")]
/// public double? GatheringWeight { get; set; } = 0.25;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class ConfigConstraintGroupAttribute : Attribute, IServiceAttribute
{
    /// <summary>
    /// The name of the constraint group this property belongs to.
    /// Must match a <see cref="ConfigConstraintGroupDefinitionAttribute"/> on the containing class.
    /// </summary>
    public string GroupName { get; }

    /// <summary>
    /// Creates a constraint group membership attribute.
    /// </summary>
    /// <param name="groupName">The constraint group name. Must not be null or empty.</param>
    /// <exception cref="ArgumentNullException">Thrown when groupName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when groupName is empty or whitespace.</exception>
    public ConfigConstraintGroupAttribute(string groupName)
    {
        if (groupName == null)
            throw new ArgumentNullException(nameof(groupName));
        if (string.IsNullOrWhiteSpace(groupName))
            throw new ArgumentException("Group name must not be empty or whitespace.", nameof(groupName));

        GroupName = groupName;
    }
}
