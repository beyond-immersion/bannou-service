using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.BannouService.TestUtilities;

// ⛔ FROZEN FILE — DO NOT MODIFY WITHOUT EXPLICIT USER PERMISSION ⛔
// This validator enforces telemetry metric ownership across ALL services.
// Changes to validation logic affect every class with [BannouService] attributes.
// If a test failure appears to be a false positive, present evidence to the user and WAIT.

/// <summary>
/// Validates that services owning telemetry metrics inject ITelemetryProvider.
/// Mirrors <see cref="StateStoreKeyValidator.ValidateStoreReferences"/> for metrics:
/// if <see cref="TelemetryMetrics.Metadata"/> declares metrics for a service,
/// that service (or a type in its assembly) must inject <see cref="ITelemetryProvider"/>.
/// </summary>
public static class TelemetryMetricValidator
{
    /// <summary>
    /// Validates that a service type references its telemetry metrics.
    /// Checks that if TelemetryMetrics.Metadata declares metrics for this service,
    /// the service injects ITelemetryProvider in its constructor.
    /// </summary>
    /// <typeparam name="TService">The service type to validate.</typeparam>
    public static void ValidateMetricReferences<TService>() where TService : class
    {
        ValidateMetricReferences(typeof(TService));
    }

    /// <summary>
    /// Validates that a service type references its telemetry metrics.
    /// </summary>
    /// <param name="serviceType">The service type to validate.</param>
    public static void ValidateMetricReferences(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        // Get the service name from [BannouService] attribute
        var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
        if (attr == null)
        {
            return; // Not a BannouService — nothing to validate
        }

        var servicePascal = KebabToPascalCase(attr.Name);

        // Find all metrics declared for this service
        var serviceMetrics = TelemetryMetrics.Metadata
            .Where(kvp => kvp.Value.Service == servicePascal)
            .ToList();

        if (serviceMetrics.Count == 0)
        {
            return; // No metrics declared for this service
        }

        // Check that the service constructor accepts ITelemetryProvider
        var constructors = serviceType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var hasProvider = constructors.Any(ctor =>
            ctor.GetParameters().Any(p => p.ParameterType == typeof(ITelemetryProvider)));

        // Also check helper services in the same assembly — some services delegate
        // metric emission to helper services (e.g., background workers, helper classes).
        if (!hasProvider)
        {
            var assemblyTypes = serviceType.Assembly.GetTypes();
            var anyTypeHasProvider = assemblyTypes.Any(t =>
                t != serviceType &&
                !t.IsAbstract &&
                !t.IsInterface &&
                t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .Any(ctor => ctor.GetParameters()
                        .Any(p => p.ParameterType == typeof(ITelemetryProvider))));

            Assert.True(
                anyTypeHasProvider,
                $"{serviceType.Name} has {serviceMetrics.Count} telemetry metric(s) declared " +
                $"({string.Join(", ", serviceMetrics.Select(m => m.Key))}) " +
                "but neither it nor any type in its assembly injects ITelemetryProvider. " +
                "Either emit the metrics or remove them from schemas/telemetry-metrics.yaml.");
        }
    }

    /// <summary>
    /// Converts kebab-case to PascalCase (e.g., "character-history" → "CharacterHistory").
    /// </summary>
    private static string KebabToPascalCase(string kebab)
    {
        return string.Concat(
            kebab.Split('-')
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
