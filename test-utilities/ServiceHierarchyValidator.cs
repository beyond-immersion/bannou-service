using System.Reflection;
using System.Text.RegularExpressions;
using BeyondImmersion.BannouService.Attributes;
using Xunit;

namespace BeyondImmersion.BannouService.TestUtilities;

/// <summary>
/// Validates that service dependencies follow the service hierarchy rules per SERVICE_HIERARCHY.md.
/// Services may only depend on services in lower layers (lower ServiceLayer enum values).
/// </summary>
/// <remarks>
/// <para>
/// This validator can be used in two contexts:
/// <list type="bullet">
///   <item><b>Unit tests:</b> Validate all built-in services at build time using reflection</item>
///   <item><b>Runtime:</b> Validate third-party plugins during PluginLoader startup</item>
/// </list>
/// </para>
/// <para>
/// <b>HIERARCHY RULES:</b>
/// <list type="bullet">
///   <item>L0 (Infrastructure) → Cannot depend on L1-L5</item>
///   <item>L1 (AppFoundation) → Can depend on L0, L1</item>
///   <item>L2 (GameFoundation) → Can depend on L0, L1, L2</item>
///   <item>L3 (AppFeatures) → Can depend on L0, L1, L3</item>
///   <item>L4 (GameFeatures) → Can depend on L0, L1, L2, L3, L4</item>
///   <item>L5 (Extensions) → Can depend on all layers</item>
/// </list>
/// </para>
/// </remarks>
public static partial class ServiceHierarchyValidator
{
    /// <summary>
    /// Static registry mapping service names to their layers.
    /// This is the authoritative source per SERVICE_HIERARCHY.md.
    /// </summary>
    private static readonly Dictionary<string, ServiceLayer> ServiceLayerRegistry = new(StringComparer.OrdinalIgnoreCase)
    {
        // L0: Infrastructure
        { "telemetry", ServiceLayer.Infrastructure },
        { "state", ServiceLayer.Infrastructure },
        { "messaging", ServiceLayer.Infrastructure },
        { "mesh", ServiceLayer.Infrastructure },

        // L1: App Foundation
        { "account", ServiceLayer.AppFoundation },
        { "auth", ServiceLayer.AppFoundation },
        { "connect", ServiceLayer.AppFoundation },
        { "permission", ServiceLayer.AppFoundation },
        { "contract", ServiceLayer.AppFoundation },
        { "resource", ServiceLayer.AppFoundation },

        // L2: Game Foundation
        { "game-service", ServiceLayer.GameFoundation },
        { "realm", ServiceLayer.GameFoundation },
        { "character", ServiceLayer.GameFoundation },
        { "species", ServiceLayer.GameFoundation },
        { "location", ServiceLayer.GameFoundation },
        { "relationship-type", ServiceLayer.GameFoundation },
        { "relationship", ServiceLayer.GameFoundation },
        { "subscription", ServiceLayer.GameFoundation },
        { "currency", ServiceLayer.GameFoundation },
        { "item", ServiceLayer.GameFoundation },
        { "inventory", ServiceLayer.GameFoundation },
        { "game-session", ServiceLayer.GameFoundation },

        // L3: App Features
        { "asset", ServiceLayer.AppFeatures },
        { "orchestrator", ServiceLayer.AppFeatures },
        { "documentation", ServiceLayer.AppFeatures },
        { "website", ServiceLayer.AppFeatures },

        // L4: Game Features (default for unknown services)
        { "actor", ServiceLayer.GameFeatures },
        { "analytics", ServiceLayer.GameFeatures },
        { "behavior", ServiceLayer.GameFeatures },
        { "mapping", ServiceLayer.GameFeatures },
        { "scene", ServiceLayer.GameFeatures },
        { "matchmaking", ServiceLayer.GameFeatures },
        { "leaderboard", ServiceLayer.GameFeatures },
        { "achievement", ServiceLayer.GameFeatures },
        { "voice", ServiceLayer.GameFeatures },
        { "save-load", ServiceLayer.GameFeatures },
        { "music", ServiceLayer.GameFeatures },
        { "escrow", ServiceLayer.GameFeatures },
        { "character-personality", ServiceLayer.GameFeatures },
        { "character-history", ServiceLayer.GameFeatures },
        { "character-encounter", ServiceLayer.GameFeatures },
        { "realm-history", ServiceLayer.GameFeatures },
    };

    /// <summary>
    /// Validates that a service type's dependencies follow the service hierarchy.
    /// Throws an assertion failure if any constructor parameter is a client from a higher layer.
    /// </summary>
    /// <typeparam name="TService">The service type to validate.</typeparam>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown when a hierarchy violation is detected.</exception>
    public static void ValidateServiceHierarchy<TService>() where TService : class
    {
        ValidateServiceHierarchy(typeof(TService));
    }

    /// <summary>
    /// Validates that a service type's dependencies follow the service hierarchy.
    /// Throws an assertion failure if any constructor parameter is a client from a higher layer.
    /// </summary>
    /// <param name="serviceType">The service type to validate.</param>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown when a hierarchy violation is detected.</exception>
    public static void ValidateServiceHierarchy(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        var violations = GetHierarchyViolations(serviceType);

        if (violations.Count > 0)
        {
            var firstViolation = violations[0];
            var violationMessages = string.Join(Environment.NewLine, violations.Select(v =>
                $"  - {v.ParameterName}: {v.ClientType.Name} (L{(int)v.ClientLayer / 100} {v.ClientLayer}) " +
                $"is higher than service layer (L{(int)v.ServiceLayer / 100} {v.ServiceLayer})"));

            Assert.Fail(
                $"SERVICE HIERARCHY VIOLATION in {serviceType.Name}:{Environment.NewLine}" +
                $"Service is {firstViolation.ServiceLayer} (L{(int)firstViolation.ServiceLayer / 100}), but depends on higher-layer clients:{Environment.NewLine}" +
                violationMessages + Environment.NewLine +
                "See SERVICE_HIERARCHY.md for dependency rules.");
        }
    }

    /// <summary>
    /// Gets all hierarchy violations for a service type.
    /// Used by both unit tests (via Assert) and runtime validation (via PluginLoader).
    /// </summary>
    /// <param name="serviceType">The service type to check.</param>
    /// <returns>List of violations, empty if the service is compliant.</returns>
    public static List<HierarchyViolation> GetHierarchyViolations(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        var violations = new List<HierarchyViolation>();

        // Get the service's layer from [BannouService] attribute
        var bannouServiceAttr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
        if (bannouServiceAttr == null)
        {
            // Not a Bannou service - skip validation
            return violations;
        }

        var serviceLayer = bannouServiceAttr.Layer;
        var serviceName = bannouServiceAttr.Name;

        // Get constructor parameters
        var constructors = serviceType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length == 0)
            return violations;

        var ctor = constructors[0];
        var parameters = ctor.GetParameters();

        foreach (var param in parameters)
        {
            var paramType = param.ParameterType;

            // Check if this is a service client interface (I*Client)
            if (!IsServiceClientInterface(paramType))
                continue;

            // Extract service name from client type (IAccountClient → account)
            var clientServiceName = ExtractServiceNameFromClientType(paramType);
            if (clientServiceName == null)
                continue;

            // Get the client's service layer
            var clientLayer = GetServiceLayer(clientServiceName);

            // Check for violation: client layer higher than service layer
            // Note: L3 (AppFeatures) cannot depend on L2 (GameFoundation) - they're separate branches
            if (IsHierarchyViolation(serviceLayer, clientLayer))
            {
                violations.Add(new HierarchyViolation
                {
                    ServiceType = serviceType,
                    ServiceName = serviceName,
                    ServiceLayer = serviceLayer,
                    ParameterName = param.Name ?? "unknown",
                    ClientType = paramType,
                    ClientServiceName = clientServiceName,
                    ClientLayer = clientLayer
                });
            }
        }

        return violations;
    }

    /// <summary>
    /// Checks if the dependency from serviceLayer to clientLayer violates the hierarchy.
    /// </summary>
    private static bool IsHierarchyViolation(ServiceLayer serviceLayer, ServiceLayer clientLayer)
    {
        // Extensions (L5) can depend on anything
        if (serviceLayer == ServiceLayer.Extensions)
            return false;

        // AppFeatures (L3) CANNOT depend on GameFoundation (L2) or GameFeatures (L4)
        // They're separate branches in the hierarchy
        if (serviceLayer == ServiceLayer.AppFeatures)
        {
            return clientLayer == ServiceLayer.GameFoundation ||
                   clientLayer == ServiceLayer.GameFeatures ||
                   clientLayer == ServiceLayer.Extensions;
        }

        // For other layers, simply check if client is in a higher layer
        return clientLayer > serviceLayer;
    }

    /// <summary>
    /// Determines if a type is a service client interface (I*Client).
    /// </summary>
    private static bool IsServiceClientInterface(Type type)
    {
        if (!type.IsInterface)
            return false;

        var name = type.Name;
        return name.StartsWith('I') && name.EndsWith("Client");
    }

    /// <summary>
    /// Extracts the service name from a client interface type.
    /// IAccountClient → account
    /// IGameSessionClient → game-session
    /// </summary>
    private static string? ExtractServiceNameFromClientType(Type clientType)
    {
        var name = clientType.Name;

        // Remove I prefix and Client suffix
        if (!name.StartsWith('I') || !name.EndsWith("Client"))
            return null;

        var servicePascal = name[1..^6]; // Remove 'I' and 'Client'

        // Convert PascalCase to kebab-case (GameSession → game-session)
        return PascalToKebabCase(servicePascal);
    }

    /// <summary>
    /// Converts PascalCase to kebab-case.
    /// GameSession → game-session
    /// CharacterPersonality → character-personality
    /// </summary>
    private static string PascalToKebabCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;

        // Insert hyphen before each uppercase letter (except first), then lowercase
        var kebab = PascalToKebabRegex().Replace(pascalCase, "-$1");
        return kebab.ToLowerInvariant();
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex PascalToKebabRegex();

    /// <summary>
    /// Gets the service layer for a service name.
    /// Returns the registered layer, or GameFeatures as default for unknown services.
    /// </summary>
    public static ServiceLayer GetServiceLayer(string serviceName)
    {
        if (ServiceLayerRegistry.TryGetValue(serviceName, out var layer))
            return layer;

        // Unknown services default to GameFeatures (most permissive for new services)
        return ServiceLayer.GameFeatures;
    }

    /// <summary>
    /// Validates all service types in an assembly.
    /// Useful for batch validation of all plugins.
    /// </summary>
    /// <param name="assembly">The assembly to scan for services.</param>
    /// <returns>Dictionary of service type to violations (empty list means compliant).</returns>
    public static Dictionary<Type, List<HierarchyViolation>> ValidateAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var results = new Dictionary<Type, List<HierarchyViolation>>();

        foreach (var type in assembly.GetTypes())
        {
            var attr = type.GetCustomAttribute<BannouServiceAttribute>();
            if (attr == null)
                continue;

            var violations = GetHierarchyViolations(type);
            results[type] = violations;
        }

        return results;
    }

    /// <summary>
    /// Represents a service hierarchy violation.
    /// </summary>
    public class HierarchyViolation
    {
        /// <summary>The service type with the violation.</summary>
        public required Type ServiceType { get; init; }

        /// <summary>The service name from [BannouService] attribute.</summary>
        public required string ServiceName { get; init; }

        /// <summary>The service's declared layer.</summary>
        public required ServiceLayer ServiceLayer { get; init; }

        /// <summary>The constructor parameter name.</summary>
        public required string ParameterName { get; init; }

        /// <summary>The client interface type being injected.</summary>
        public required Type ClientType { get; init; }

        /// <summary>The service name the client is for.</summary>
        public required string ClientServiceName { get; init; }

        /// <summary>The client's service layer.</summary>
        public required ServiceLayer ClientLayer { get; init; }

        /// <inheritdoc/>
        public override string ToString() =>
            $"{ServiceName} (L{(int)ServiceLayer / 100}) depends on {ClientServiceName} (L{(int)ClientLayer / 100}) via {ParameterName}";
    }
}
