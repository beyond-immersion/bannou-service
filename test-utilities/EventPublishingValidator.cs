using System.Reflection;
using BeyondImmersion.BannouService.Attributes;

namespace BeyondImmersion.BannouService.TestUtilities;

/// <summary>
/// Validates event publishing patterns per FOUNDATION TENETS:
/// - Services with generated *EventPublisher extension classes must call
///   every generated publish method from somewhere in the plugin assembly.
/// - This ensures every declared event topic in x-event-publications is
///   actually published by the service.
/// </summary>
public static class EventPublishingValidator
{
    /// <summary>
    /// Finds the generated *EventPublisher extension class for a service type.
    /// Returns null if no publisher class exists (service has no published events).
    /// </summary>
    /// <param name="serviceType">The [BannouService]-attributed service type.</param>
    /// <returns>The publisher class type and its public static method names, or null.</returns>
    public static EventPublisherInfo? GetEventPublisherInfo(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
        if (attr == null)
            return null;

        var publisherClass = FindEventPublisherClass(serviceType);
        if (publisherClass == null)
            return null;

        var methods = publisherClass
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.StartsWith("Publish", StringComparison.Ordinal)
                        && m.Name.EndsWith("Async", StringComparison.Ordinal))
            .Select(m => m.Name)
            .ToArray();

        if (methods.Length == 0)
            return null;

        return new EventPublisherInfo(publisherClass, methods);
    }

    /// <summary>
    /// Finds the companion *EventPublisher extension class for a service type.
    /// Searches the same assembly and namespace for a static class ending in "EventPublisher".
    /// </summary>
    private static Type? FindEventPublisherClass(Type serviceType)
    {
        var serviceNamespace = serviceType.Namespace;
        if (serviceNamespace == null)
            return null;

        var candidates = serviceType.Assembly.GetTypes()
            .Where(t => t.Namespace == serviceNamespace
                        && t.IsClass
                        && t.IsAbstract  // static classes are abstract + sealed
                        && t.IsSealed
                        && t.Name.EndsWith("EventPublisher", StringComparison.Ordinal))
            .ToArray();

        return candidates.Length == 1 ? candidates[0] : null;
    }
}

/// <summary>
/// Information about a generated event publisher extension class.
/// </summary>
/// <param name="PublisherType">The generated static extension class type.</param>
/// <param name="MethodNames">Names of all generated Publish*Async extension methods.</param>
public record EventPublisherInfo(Type PublisherType, string[] MethodNames);
