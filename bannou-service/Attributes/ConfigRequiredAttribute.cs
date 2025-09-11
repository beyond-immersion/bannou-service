using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// The configuration property is required for the service to function.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class ConfigRequiredAttribute : RequiredAttribute, IServiceAttribute
{
}
