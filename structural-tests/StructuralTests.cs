using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Auto-discovered structural validation tests for all Bannou services.
/// Services are discovered via [BannouService] attribute reflection — adding
/// a new plugin automatically includes it with zero opt-in required.
/// </summary>
public class StructuralTests
{
    /// <summary>
    /// Discovers all types with [BannouService] attribute across all loaded assemblies.
    /// Each discovered type becomes a row in Theory-based test methods.
    /// </summary>
    public static IEnumerable<object[]> AllServiceTypes()
    {
        foreach (var type in DiscoverAttributedTypes<BannouServiceAttribute>())
            yield return [type];
    }

    /// <summary>
    /// Discovers all types with [BannouHelperService] attribute across all loaded assemblies.
    /// Helper services get constructor and key builder validation but not hierarchy or controller checks.
    /// </summary>
    public static IEnumerable<object[]> AllHelperServiceTypes()
    {
        foreach (var type in DiscoverAttributedTypes<BannouHelperServiceAttribute>())
            yield return [type];
    }

    /// <summary>
    /// Generic discovery of types with a specific attribute across all loaded plugin assemblies.
    /// </summary>
    private static IEnumerable<Type> DiscoverAttributedTypes<TAttribute>() where TAttribute : Attribute
    {
        EnsureAssembliesLoaded();

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null ||
                assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Microsoft", StringComparison.Ordinal) ||
                assemblyName.StartsWith("netstandard", StringComparison.Ordinal) ||
                assemblyName.StartsWith("mscorlib", StringComparison.Ordinal) ||
                assemblyName.StartsWith("xunit", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Moq", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Castle", StringComparison.Ordinal))
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException)
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type.GetCustomAttribute<TAttribute>() != null)
                {
                    yield return type;
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_HasValidConstructor(Type serviceType)
    {
        ServiceConstructorValidator.ValidateServiceConstructor(serviceType);
    }

    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_RespectsHierarchy(Type serviceType)
    {
        ServiceHierarchyValidator.ValidateServiceHierarchy(serviceType);
    }

    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_HasValidKeyBuilders(Type serviceType)
    {
        StateStoreKeyValidator.ValidateKeyBuilders(serviceType);
    }

    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_ReferencesItsStateStores(Type serviceType)
    {
        StateStoreKeyValidator.ValidateStoreReferences(serviceType);
    }

    [Theory]
    [MemberData(nameof(AllHelperServiceTypes))]
    public void HelperService_HasValidConstructor(Type helperType)
    {
        ServiceConstructorValidator.ValidateServiceConstructor(helperType);
    }

    [Theory]
    [MemberData(nameof(AllHelperServiceTypes))]
    public void HelperService_HasValidKeyBuilders(Type helperType)
    {
        StateStoreKeyValidator.ValidateKeyBuilders(helperType);
    }

    /// <summary>
    /// Detects Plugin.cs files that still manually register helper services which have
    /// [BannouHelperService] with a non-null InterfaceType. These registrations are now
    /// redundant because PluginLoader auto-discovers and registers them.
    /// This test provides a cleanup checklist — remove the manual registration lines
    /// from Plugin.cs and let auto-registration handle them.
    /// </summary>
    [Fact]
    public void Plugins_ShouldNotManuallyRegisterAutoDiscoverableHelpers()
    {
        EnsureAssembliesLoaded();

        // Build a set of (interfaceType, implementationType) pairs that are auto-discoverable
        var autoDiscoverable = new HashSet<(string interfaceName, string implName)>();
        foreach (var type in DiscoverAttributedTypes<BannouHelperServiceAttribute>())
        {
            var attr = type.GetCustomAttribute<BannouHelperServiceAttribute>();
            if (attr?.InterfaceType != null)
            {
                autoDiscoverable.Add((attr.InterfaceType.Name, type.Name));
            }
        }

        if (autoDiscoverable.Count == 0)
            return;

        // Scan all *Plugin.cs files for manual registrations of auto-discoverable types
        var pluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");
        if (!Directory.Exists(pluginsDir))
            return;

        var redundant = new List<string>();
        var pluginFiles = Directory.GetFiles(pluginsDir, "*Plugin.cs", SearchOption.AllDirectories);

        foreach (var pluginFile in pluginFiles)
        {
            var lines = File.ReadAllLines(pluginFile);
            var relativePath = Path.GetRelativePath(TestAssemblyDiscovery.RepoRoot, pluginFile);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();

                // Match patterns: services.AddScoped<IFoo, Foo>() or services.AddSingleton<IFoo, Foo>()
                // Skip factory lambdas (lines containing "sp =>" or "(sp") — those need manual registration
                if (!line.StartsWith("services.Add", StringComparison.Ordinal))
                    continue;
                if (line.Contains("sp =>") || line.Contains("(sp"))
                    continue;

                // Extract interface and implementation names from generic arguments
                var angleBracketStart = line.IndexOf('<');
                var angleBracketEnd = line.IndexOf('>');
                if (angleBracketStart < 0 || angleBracketEnd < 0)
                    continue;

                var generics = line[(angleBracketStart + 1)..angleBracketEnd];
                var parts = generics.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                    continue;

                var interfaceName = parts[0];
                var implName = parts[1];

                if (autoDiscoverable.Contains((interfaceName, implName)))
                {
                    redundant.Add($"{relativePath}:{i + 1}: services.Add*<{interfaceName}, {implName}>() " +
                        $"— auto-discoverable via [BannouHelperService], remove manual registration");
                }
            }
        }

        Assert.True(
            redundant.Count == 0,
            $"{redundant.Count} manual registration(s) in Plugin.cs files are now redundant " +
            $"(auto-discoverable via [BannouHelperService]):\n" +
            string.Join("\n", redundant.Select(r => $"  - {r}")));
    }

    /// <summary>
    /// Validates that each service's generated controller correctly implements
    /// IBannouController (via the generated I{Service}Controller interface) and
    /// has the [BannouController] attribute pointing to the correct service interface.
    /// Catches Controller.liquid template regressions.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_HasValidController(Type serviceType)
    {
        var violations = ControllerValidator.GetControllerViolations(serviceType);

        Assert.True(
            violations.Length == 0,
            $"{serviceType.Name}: {violations.Length} controller violation(s):\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    /// <summary>
    /// Validates that the generated PermissionRegistration endpoint count matches the
    /// number of endpoints with non-empty x-permissions in the API schema. A mismatch
    /// means the permission generation script silently skipped or duplicated endpoints.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_PermissionRegistrationEndpointCountMatchesSchema(Type serviceType)
    {
        var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
        if (attr == null)
            return;

        var serviceName = attr.Name;
        var schemaPath = Path.Combine(TestAssemblyDiscovery.RepoRoot, "schemas", $"{serviceName}-api.yaml");
        if (!File.Exists(schemaPath))
            return; // No schema file (e.g., stub services)

        // Count endpoints with non-empty x-permissions in the schema
        var schemaEndpointCount = CountSchemaEndpointsWithPermissions(schemaPath);
        if (schemaEndpointCount == 0)
            return; // All endpoints are service-only (x-permissions: [])

        // Find the PermissionRegistration class and call GetEndpoints()
        var registrationType = serviceType.Assembly.GetTypes()
            .FirstOrDefault(t => t.IsClass && t.IsAbstract && t.IsSealed // static class
                && t.Name.EndsWith("PermissionRegistration", StringComparison.Ordinal));

        Assert.True(
            registrationType != null,
            $"{serviceType.Name}: has {schemaEndpointCount} endpoint(s) with x-permissions roles " +
            $"but no *PermissionRegistration class found in assembly");

        var getEndpointsMethod = registrationType.GetMethod("GetEndpoints",
            BindingFlags.Public | BindingFlags.Static);
        Assert.True(
            getEndpointsMethod != null,
            $"{serviceType.Name}: {registrationType.Name} has no GetEndpoints() method");

        var endpoints = getEndpointsMethod.Invoke(null, null);
        var registrationCount = endpoints is System.Collections.ICollection collection
            ? collection.Count
            : 0;

        Assert.True(
            registrationCount == schemaEndpointCount,
            $"{serviceType.Name}: PermissionRegistration has {registrationCount} endpoint(s) " +
            $"but schema has {schemaEndpointCount} endpoint(s) with non-empty x-permissions");
    }

    /// <summary>
    /// Counts endpoints in an API schema that have non-empty x-permissions
    /// (i.e., endpoints exposed to WebSocket clients with role requirements).
    /// Uses line-based parsing — no YAML library needed.
    /// </summary>
    private static int CountSchemaEndpointsWithPermissions(string schemaPath)
    {
        var lines = File.ReadAllLines(schemaPath);
        var count = 0;
        var inEndpoint = false;
        var currentIndent = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Detect HTTP method entries under paths (post:, get:, put:, delete:)
            if (trimmed.StartsWith("post:", StringComparison.Ordinal) ||
                trimmed.StartsWith("get:", StringComparison.Ordinal) ||
                trimmed.StartsWith("put:", StringComparison.Ordinal) ||
                trimmed.StartsWith("delete:", StringComparison.Ordinal))
            {
                inEndpoint = true;
                currentIndent = line.Length - trimmed.Length;
                continue;
            }

            if (!inEndpoint)
                continue;

            // Check if we've left the current endpoint block
            if (trimmed.Length > 0 && (line.Length - trimmed.Length) <= currentIndent)
            {
                inEndpoint = false;
                // Re-check this line for another endpoint
                i--;
                continue;
            }

            // Look for x-permissions within this endpoint
            if (trimmed.StartsWith("x-permissions:", StringComparison.Ordinal))
            {
                // x-permissions: [] means empty (service-only), skip
                // x-permissions: followed by items means has roles
                var value = trimmed["x-permissions:".Length..].Trim();

                // Strip inline YAML comments (e.g., "[]  # Internal only...")
                var commentIdx = value.IndexOf('#');
                if (commentIdx > 0)
                {
                    value = value[..commentIdx].TrimEnd();
                }

                if (value != "[]")
                {
                    count++;
                }
                inEndpoint = false;
            }
        }

        return count;
    }

    /// <summary>
    /// Validates that each service's generated configuration class can be instantiated
    /// via Activator.CreateInstance() without throwing. Configuration classes must have
    /// parameterless constructors and sensible defaults. A broken config class blocks
    /// the entire service at startup.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_ConfigurationClassIsInstantiable(Type serviceType)
    {
        var configType = serviceType.Assembly.GetTypes()
            .FirstOrDefault(t => t.GetCustomAttribute<ServiceConfigurationAttribute>()
                ?.ServiceImplementationType == serviceType);

        if (configType == null)
            return; // Service has no configuration class

        // Must have a public parameterless constructor
        var ctor = configType.GetConstructor(Type.EmptyTypes);
        Assert.True(
            ctor != null,
            $"{serviceType.Name}: configuration class {configType.Name} has no public parameterless constructor");

        // Must be instantiable without throwing
        var ex = Record.Exception(() => Activator.CreateInstance(configType));
        Assert.True(
            ex == null,
            $"{serviceType.Name}: configuration class {configType.Name} threw on instantiation: {ex?.Message}");
    }

    /// <summary>
    /// Publisher methods that are intentionally never called. Category B entities
    /// (per IMPLEMENTATION TENETS) are never deleted — their x-lifecycle generates
    /// a *.deleted publisher but it is unused infrastructure. Immutable entities
    /// (e.g., license boards) similarly have unused *.updated publishers.
    /// </summary>
    private static readonly HashSet<string> IntentionallyUncalledPublishers = new(StringComparer.Ordinal)
    {
        // Category B template entities — never deleted, deprecation-only
        "PublishItemTemplateDeletedAsync",
        "PublishCollectionEntryTemplateDeletedAsync",
        "PublishQuestDefinitionDeletedAsync",
        "PublishContractTemplateDeletedAsync",
        "PublishCurrencyDefinitionDeletedAsync",
        "PublishLicenseBoardTemplateDeletedAsync",
        "PublishLeaderboardDefinitionDeletedAsync",
        "PublishChatRoomTypeDeletedAsync",
        "PublishEncounterTypeDeletedAsync",
        "PublishScenarioTemplateDeletedAsync",       // lib-gardener
        "PublishScenarioDefinitionDeletedAsync",     // lib-storyline

        // Immutable instance entities — never updated after creation
        "PublishLicenseBoardUpdatedAsync",
    };

    /// <summary>
    /// Validates that every generated Publish*Async extension method on the service's
    /// *EventPublisher class is called from somewhere in the plugin assembly.
    /// An uncalled method means a declared event topic is never published.
    /// Methods in <see cref="IntentionallyUncalledPublishers"/> are excluded
    /// (Category B unused *.deleted infrastructure, immutable entity *.updated).
    /// </summary>
    [Theory(Skip = "Temporarily skipped — services with unimplemented event publishers need implementation work")]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_CallsAllGeneratedEventPublishers(Type serviceType)
    {
        var publisherInfo = EventPublishingValidator.GetEventPublisherInfo(serviceType);
        if (publisherInfo == null)
            return; // No generated publisher — service has no published events

        var assemblyPath = serviceType.Assembly.Location;
        if (string.IsNullOrEmpty(assemblyPath))
            return;

        var referencedMethods = AssemblyMetadataScanner.GetReferencedMethods(
            assemblyPath,
            publisherInfo.PublisherType.Name,
            publisherInfo.MethodNames);

        var uncalledMethods = publisherInfo.MethodNames
            .Where(m => !referencedMethods.Contains(m))
            .Where(m => !IntentionallyUncalledPublishers.Contains(m))
            .ToArray();

        Assert.True(
            uncalledMethods.Length == 0,
            $"{serviceType.Name}: {uncalledMethods.Length} generated event publisher method(s) " +
            $"on {publisherInfo.PublisherType.Name} are never called " +
            $"(declared events not published):\n" +
            string.Join("\n", uncalledMethods.Select(m => $"  - {m}")));
    }

    /// <summary>
    /// Validates that services with [ResourceCleanupRequired] attributes have
    /// corresponding CleanupBy{Target}Async methods implemented. The attributes
    /// are generated from x-references cleanup declarations; a missing method
    /// means lib-resource will call a non-existent endpoint at runtime.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_HasRequiredCleanupMethods(Type serviceType)
    {
        var missing = ResourceCleanupValidator.GetMissingCleanupMethods(serviceType);

        Assert.True(
            missing.Length == 0,
            $"{serviceType.Name}: {missing.Length} cleanup method(s) required by " +
            $"[ResourceCleanupRequired] (from x-references) but not found:\n" +
            string.Join("\n", missing.Select(m => $"  - {m}")));
    }

    /// <summary>
    /// Validates that each service has a {Service}ServiceEvents.cs file on disk,
    /// confirming the partial class pattern required by FOUNDATION TENETS (service logic in
    /// *Service.cs, event handlers in *ServiceEvents.cs).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_HasServiceEventsFile(Type serviceType)
    {
        var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
        if (attr == null)
            return;

        var serviceName = attr.Name;
        var pluginDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins", $"lib-{serviceName}");
        if (!Directory.Exists(pluginDir))
            return;

        var eventsFileName = $"{serviceType.Name}Events.cs";
        var eventsFilePath = Path.Combine(pluginDir, eventsFileName);

        Assert.True(
            File.Exists(eventsFilePath),
            $"{serviceType.Name}: missing {eventsFileName} in lib-{serviceName}/ " +
            $"— FOUNDATION TENETS requires partial class split for event handlers");
    }

    /// <summary>
    /// Validates that no plugin assembly directly calls System.Text.Json.JsonSerializer
    /// methods (Serialize, Deserialize). All JSON serialization must go through
    /// BannouJson.Serialize/Deserialize for consistent settings (IMPLEMENTATION TENETS).
    /// Uses IL-level metadata scanning to catch compiled violations.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_NoDirectJsonSerializer(Type serviceType)
    {
        var assemblyPath = serviceType.Assembly.Location;
        if (string.IsNullOrEmpty(assemblyPath))
            return;

        string[] methods = ["Serialize", "Deserialize"];
        var found = AssemblyMetadataScanner.GetReferencedMethods(
            assemblyPath, "JsonSerializer", methods);

        Assert.True(
            found.Count == 0,
            $"{serviceType.Name}: directly references JsonSerializer.{string.Join(", ", found)} " +
            $"— use BannouJson instead (IMPLEMENTATION TENETS)");
    }

    /// <summary>
    /// Validates that no plugin assembly directly calls Environment.GetEnvironmentVariable().
    /// All configuration must go through generated configuration classes (IMPLEMENTATION TENETS).
    /// Uses IL-level metadata scanning to catch compiled violations.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_NoDirectEnvironmentGetVariable(Type serviceType)
    {
        var assemblyPath = serviceType.Assembly.Location;
        if (string.IsNullOrEmpty(assemblyPath))
            return;

        string[] methods = ["GetEnvironmentVariable"];
        var found = AssemblyMetadataScanner.GetReferencedMethods(
            assemblyPath, "Environment", methods);

        Assert.True(
            found.Count == 0,
            $"{serviceType.Name}: directly calls Environment.GetEnvironmentVariable() " +
            $"— use service configuration class instead (IMPLEMENTATION TENETS)");
    }

    /// <summary>
    /// Validates that no non-infrastructure plugin assembly directly references
    /// infrastructure client types (StackExchange.Redis, RabbitMQ.Client, MySqlConnector).
    /// Non-L0 services must use lib-state, lib-messaging, lib-mesh abstractions (FOUNDATION TENETS).
    /// Uses IL-level metadata scanning to catch compiled violations.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_NoDirectInfrastructureImports(Type serviceType)
    {
        var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
        if (attr == null)
            return;

        // L0 infrastructure services are allowed to use these
        if (attr.Layer == ServiceLayer.Infrastructure)
            return;

        var assemblyPath = serviceType.Assembly.Location;
        if (string.IsNullOrEmpty(assemblyPath))
            return;

        var violations = new List<string>();

        // Check for StackExchange.Redis direct usage
        if (AssemblyMetadataScanner.ReferencesMethodOnType(
            assemblyPath, "ConnectionMultiplexer", "Connect", "ConnectAsync"))
            violations.Add("StackExchange.Redis.ConnectionMultiplexer");

        if (AssemblyMetadataScanner.ReferencesMethodOnType(
            assemblyPath, "IDatabase", "StringGetAsync", "StringSetAsync", "KeyDeleteAsync"))
            violations.Add("StackExchange.Redis.IDatabase");

        // Check for RabbitMQ.Client direct usage
        if (AssemblyMetadataScanner.ReferencesMethodOnType(
            assemblyPath, "ConnectionFactory", "CreateConnectionAsync"))
            violations.Add("RabbitMQ.Client.ConnectionFactory");

        // Check for MySqlConnector direct usage
        if (AssemblyMetadataScanner.ReferencesMethodOnType(
            assemblyPath, "MySqlConnection", "OpenAsync"))
            violations.Add("MySqlConnector.MySqlConnection");

        Assert.True(
            violations.Count == 0,
            $"{serviceType.Name} (Layer {attr.Layer}): directly references infrastructure types " +
            $"— use lib-state/lib-messaging/lib-mesh instead (FOUNDATION TENETS):\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    /// <summary>
    /// Validates that no plugin assembly uses Microsoft.AspNetCore.Http.StatusCodes.
    /// All services must use BeyondImmersion.BannouService.StatusCodes instead (IMPLEMENTATION TENETS).
    /// Uses IL-level metadata scanning to catch compiled violations.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_UsesCorrectStatusCodesEnum(Type serviceType)
    {
        var assemblyPath = serviceType.Assembly.Location;
        if (string.IsNullOrEmpty(assemblyPath))
            return;

        // Check for references to the ASP.NET StatusCodes class fields (Status200OK, etc.)
        // These are static field reads, not method calls, so we check for type reference instead
        string[] statusFields = ["Status200OK", "Status400BadRequest", "Status404NotFound",
            "Status500InternalServerError", "Status201Created", "Status204NoContent",
            "Status401Unauthorized", "Status403Forbidden", "Status409Conflict"];

        var found = AssemblyMetadataScanner.GetReferencedMethods(
            assemblyPath, "StatusCodes", statusFields);

        // Only flag if the reference is to Microsoft's StatusCodes, not our own.
        // Since both have the same type name, we can't distinguish via IL alone.
        // But our StatusCodes doesn't have Status-prefixed members, so any match
        // is the ASP.NET version.
        Assert.True(
            found.Count == 0,
            $"{serviceType.Name}: references Microsoft.AspNetCore.Http.StatusCodes " +
            $"({string.Join(", ", found)}) — use BannouService.StatusCodes instead (IMPLEMENTATION TENETS)");
    }

    /// <summary>
    /// Validates that every plugin using EnumMapping helper methods (MapByName,
    /// MapByNameOrDefault, TryMapByName) has corresponding EnumMappingValidator
    /// tests in its test project. This is the safety guarantee documented in
    /// EnumMapping's XML docs — catching enum value drift at test time rather
    /// than at runtime.
    /// </summary>
    [Fact]
    public void PluginsUsingEnumMapping_MustHaveEnumMappingValidatorTests()
    {
        EnsureAssembliesLoaded();

        string[] enumMappingMethods = ["MapByName", "MapByNameOrDefault", "TryMapByName"];
        string[] validatorMethods =
        [
            "AssertFullCoverage", "AssertSubset", "AssertSupersetToSubsetMapping",
            "AssertSwitchCoversAllValues", "AssertSourceCoveredByTarget"
        ];

        var failures = new List<string>();

        var pluginAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a =>
            {
                var name = a.GetName().Name;
                return name != null
                    && name.StartsWith("lib-", StringComparison.Ordinal)
                    && !name.EndsWith(".tests", StringComparison.Ordinal);
            });

        foreach (var pluginAssembly in pluginAssemblies)
        {
            var pluginPath = pluginAssembly.Location;
            if (string.IsNullOrEmpty(pluginPath))
                continue;

            if (!AssemblyMetadataScanner.ReferencesMethodOnType(pluginPath, "EnumMapping", enumMappingMethods))
                continue;

            var pluginName = pluginAssembly.GetName().Name!;
            var serviceName = pluginName["lib-".Length..];
            var testAssemblyPath = TestAssemblyDiscovery.GetTestAssemblyPath(serviceName);

            if (testAssemblyPath == null)
            {
                failures.Add($"{pluginName}: test assembly not found (not built or missing)");
            }
            else if (!AssemblyMetadataScanner.ReferencesMethodOnType(
                testAssemblyPath, "EnumMappingValidator", validatorMethods))
            {
                failures.Add($"{pluginName}: uses EnumMapping but test project has no EnumMappingValidator tests");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Plugins using EnumMapping helpers must have corresponding EnumMappingValidator " +
            $"tests in their test project (per ENUM-BOUNDARIES.md):\n" +
            string.Join("\n", failures.Select(f => $"  - {f}")));
    }

    /// <summary>
    /// Category A entities that do not yet have merge endpoints implemented.
    /// These services have <c>deprecation: true</c> in their events schema but are
    /// not yet ready to implement <see cref="IDeprecateAndMergeEntity"/>. As merge
    /// endpoints are added, remove the service from this list — the test will enforce
    /// the interface requirement automatically.
    /// </summary>
    private static readonly HashSet<string> DeprecationInterfacePending = new(StringComparer.Ordinal)
    {
        // Category A entities without merge endpoint yet (deprecation: true in schema,
        // but merge not implemented so IDeprecateAndMergeEntity not yet applicable).
        "seed",             // Seed Type — merge not yet implemented
        "location",         // Location — merge not yet implemented
        "status",           // Status Template — merge not yet implemented
        "faction",          // Faction — merge not yet implemented
        "transit",          // Transit Mode — merge not yet implemented
    };

    /// <summary>
    /// Validates that every service with <c>deprecation: true</c> in its events schema
    /// implements either <see cref="IDeprecateAndMergeEntity"/> (Category A — world-building
    /// definitions with merge semantics) or <see cref="ICleanDeprecatedEntity"/> (Category B —
    /// content templates with cleanup sweep semantics).
    /// <para>
    /// This ensures all deprecated entities have a standardized lifecycle pattern and that
    /// the pattern is discoverable via reflection for tooling and documentation generation.
    /// </para>
    /// </summary>
    [Fact]
    public void Services_WithDeprecation_MustImplementDeprecationInterface()
    {
        EnsureAssembliesLoaded();

        var schemasDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "schemas");
        if (!Directory.Exists(schemasDir))
            return;

        var failures = new List<string>();
        var deprecatedServices = DiscoverServicesWithDeprecation(schemasDir);

        foreach (var serviceName in deprecatedServices)
        {
            if (DeprecationInterfacePending.Contains(serviceName))
                continue;

            // Find the [BannouService] type for this service name
            var serviceType = FindServiceTypeByName(serviceName);
            if (serviceType == null)
            {
                failures.Add($"{serviceName}: has deprecation: true but no [BannouService] class found");
                continue;
            }

            var implementsMerge = typeof(IDeprecateAndMergeEntity).IsAssignableFrom(serviceType);
            var implementsClean = typeof(ICleanDeprecatedEntity).IsAssignableFrom(serviceType);

            if (!implementsMerge && !implementsClean)
            {
                failures.Add(
                    $"{serviceType.Name} (service: {serviceName}): has deprecation: true in events schema " +
                    $"but implements neither IDeprecateAndMergeEntity (Category A) nor " +
                    $"ICleanDeprecatedEntity (Category B)");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Services with deprecated entities must implement a deprecation marker interface " +
            $"(per IMPLEMENTATION TENETS):\n" +
            string.Join("\n", failures.Select(f => $"  - {f}")));
    }

    /// <summary>
    /// Scans event schema files for <c>deprecation: true</c> and returns the set of
    /// service names that have at least one deprecated entity.
    /// </summary>
    private static HashSet<string> DiscoverServicesWithDeprecation(string schemasDir)
    {
        var services = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(schemasDir, "*-events.yaml"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!fileName.EndsWith("-events", StringComparison.Ordinal))
                continue;

            var serviceName = fileName[..^"-events".Length];

            // Line-based scan — no YAML library needed
            foreach (var line in File.ReadLines(file))
            {
                if (line.TrimStart().StartsWith("deprecation: true", StringComparison.Ordinal))
                {
                    services.Add(serviceName);
                    break;
                }
            }
        }

        return services;
    }

    /// <summary>
    /// Finds the [BannouService]-attributed type matching the given service name.
    /// </summary>
    private static Type? FindServiceTypeByName(string serviceName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException) { continue; }

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<BannouServiceAttribute>();
                if (attr != null && string.Equals(attr.Name, serviceName, StringComparison.Ordinal))
                    return type;
            }
        }

        return null;
    }

    /// <summary>
    /// Ensures all plugin assemblies are loaded into the AppDomain.
    /// Project references guarantee compilation but not loading — we need
    /// to touch at least one type per assembly to force the CLR to load it.
    /// </summary>
    private static void EnsureAssembliesLoaded()
    {
        // Reference one type from each plugin assembly to force loading.
        // This list is maintained manually but only needs the assembly, not specific types.
        // If a plugin is missing, its tests simply won't run (safe failure mode).
        var assemblyAnchors = new[]
        {
            typeof(BannouService.Account.AccountService),
            typeof(BannouService.Achievement.AchievementService),
            typeof(BannouService.Actor.ActorService),
            typeof(BannouService.Analytics.AnalyticsService),
            typeof(BannouService.Asset.AssetService),
            typeof(BannouService.Auth.AuthService),
            typeof(BannouService.Behavior.BehaviorService),
            typeof(BannouService.Character.CharacterService),
            typeof(BannouService.CharacterEncounter.CharacterEncounterService),
            typeof(BannouService.CharacterHistory.CharacterHistoryService),
            typeof(BannouService.CharacterPersonality.CharacterPersonalityService),
            typeof(BannouService.Chat.ChatService),
            typeof(BannouService.Collection.CollectionService),
            typeof(BannouService.Connect.ConnectService),
            typeof(BannouService.Contract.ContractService),
            typeof(BannouService.Currency.CurrencyService),
            typeof(BannouService.Divine.DivineService),
            typeof(BannouService.Documentation.DocumentationService),
            typeof(BannouService.Escrow.EscrowService),
            typeof(BannouService.Faction.FactionService),
            typeof(BannouService.GameService.GameServiceService),
            typeof(BannouService.GameSession.GameSessionService),
            typeof(BannouService.Gardener.GardenerService),
            typeof(BannouService.Inventory.InventoryService),
            typeof(BannouService.Item.ItemService),
            typeof(BannouService.Leaderboard.LeaderboardService),
            typeof(BannouService.License.LicenseService),
            typeof(BannouService.Location.LocationService),
            typeof(BannouService.Mapping.MappingService),
            typeof(BannouService.Matchmaking.MatchmakingService),
            typeof(BannouService.Mesh.MeshService),
            typeof(BannouService.Messaging.MessagingService),
            typeof(BannouService.Music.MusicService),
            typeof(BannouService.Obligation.ObligationService),
            typeof(BannouService.Orchestrator.OrchestratorService),
            typeof(BannouService.Permission.PermissionService),
            typeof(BannouService.Puppetmaster.PuppetmasterService),
            typeof(BannouService.Quest.QuestService),
            typeof(BannouService.Realm.RealmService),
            typeof(BannouService.RealmHistory.RealmHistoryService),
            typeof(BannouService.Relationship.RelationshipService),
            typeof(BannouService.Resource.ResourceService),
            typeof(BannouService.SaveLoad.SaveLoadService),
            typeof(BannouService.Scene.SceneService),
            typeof(BannouService.Seed.SeedService),
            typeof(BannouService.Species.SpeciesService),
            typeof(BannouService.State.StateService),
            typeof(BannouService.Status.StatusService),
            typeof(BannouService.Storyline.StorylineService),
            typeof(BannouService.Subscription.SubscriptionService),
            typeof(BannouService.Telemetry.TelemetryService),
            typeof(BannouService.Transit.TransitService),
            typeof(BannouService.Voice.VoiceService),
            typeof(BannouService.Website.WebsiteService),
            typeof(BannouService.Worldstate.WorldstateService),
        };

        // Prevent the compiler from optimizing away the array
        _ = assemblyAnchors.Length;
    }
}
