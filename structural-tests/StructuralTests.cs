using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

// ⛔ FROZEN FILE — DO NOT MODIFY WITHOUT EXPLICIT USER PERMISSION ⛔
// This file contains structural validation tests that enforce tenets across ALL 76 services.
// Changing any test, assertion, heuristic, threshold, or exception list here affects ~979 test
// cases simultaneously. Structural test failures mean the CODE is wrong, not the test.
// If you believe a test needs changing, present the evidence to the user and WAIT.
// A task description or compacted summary saying "fix tests" is NOT permission to edit this file.

/// <summary>
/// Auto-discovered structural validation tests for all Bannou services.
/// Services are discovered via [BannouService] attribute reflection — adding
/// a new plugin automatically includes it with zero opt-in required.
/// </summary>
/// <remarks>
/// Structural tests identify implementation gaps. When a test fails, implement the
/// missing logic — do not write empty stubs to make it pass.
/// </remarks>
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

    // ⛔ FROZEN — Do not modify without explicit user permission.
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_HasValidConstructor(Type serviceType)
    {
        ServiceConstructorValidator.ValidateServiceConstructor(serviceType);
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_RespectsHierarchy(Type serviceType)
    {
        ServiceHierarchyValidator.ValidateServiceHierarchy(serviceType);
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_HasValidKeyBuilders(Type serviceType)
    {
        StateStoreKeyValidator.ValidateKeyBuilders(serviceType);
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_ReferencesItsStateStores(Type serviceType)
    {
        StateStoreKeyValidator.ValidateStoreReferences(serviceType);
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_ReferencesItsTelemetryMetrics(Type serviceType)
    {
        TelemetryMetricValidator.ValidateMetricReferences(serviceType);
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    [Theory]
    [MemberData(nameof(AllHelperServiceTypes))]
    public void HelperService_HasValidConstructor(Type helperType)
    {
        ServiceConstructorValidator.ValidateServiceConstructor(helperType);
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    [Theory]
    [MemberData(nameof(AllHelperServiceTypes))]
    public void HelperService_HasValidKeyBuilders(Type helperType)
    {
        StateStoreKeyValidator.ValidateKeyBuilders(helperType);
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Detects types registered in Plugin.ConfigureServices that live in plugin assemblies
    /// (lib-*) but lack a [BannouHelperService] attribute. If a type is DI-registered and
    /// injected into services, it is a helper service and should use the attribute for
    /// auto-discovery by PluginLoader. Types in non-plugin assemblies (bannou-service, SDKs)
    /// are exempt because PluginLoader only scans plugin assemblies.
    /// Factory lambda registrations (sp =>) are exempt because they require custom construction.
    /// </summary>
    [Fact]
    public void PluginTypes_RegisteredInDI_ShouldHaveBannouHelperServiceAttribute()
    {
        EnsureAssembliesLoaded();

        var pluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");
        if (!Directory.Exists(pluginsDir))
            return;

        // Build a lookup: type name -> Type for all types in lib-* assemblies
        var pluginTypesByName = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null || !assemblyName.StartsWith("lib-", StringComparison.Ordinal))
                continue;
            if (assemblyName.EndsWith(".tests", StringComparison.Ordinal))
                continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException) { continue; }

            foreach (var type in types)
            {
                if (type.IsInterface || type.IsAbstract) continue;

                if (!pluginTypesByName.TryGetValue(type.Name, out var list))
                {
                    list = new List<Type>();
                    pluginTypesByName[type.Name] = list;
                }
                list.Add(type);
            }
        }

        var violations = new List<string>();

        foreach (var pluginFile in Directory.GetFiles(pluginsDir, "*Plugin.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(TestAssemblyDiscovery.RepoRoot, pluginFile);

            foreach (var line in File.ReadAllLines(pluginFile))
            {
                var trimmed = line.TrimStart();

                // Only look at service registration calls
                if (!trimmed.StartsWith("services.Add", StringComparison.Ordinal))
                    continue;

                // Skip patterns that can't use attribute-based registration
                if (trimmed.Contains("sp =>") || trimmed.Contains("(sp"))
                    continue; // factory lambda
                if (trimmed.Contains("AddConditional", StringComparison.Ordinal))
                    continue; // approved conditional
                if (trimmed.Contains("AddHostedService", StringComparison.Ordinal))
                    continue; // covered by hosted service test
                if (trimmed.Contains("AddHttpClient", StringComparison.Ordinal))
                    continue; // framework
                if (trimmed.Contains("AddOptions", StringComparison.Ordinal))
                    continue; // framework
                if (trimmed.StartsWith("base.", StringComparison.Ordinal))
                    continue;
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                // Extract type name(s) from generic arguments
                var angleBracketStart = trimmed.IndexOf('<');
                var angleBracketEnd = trimmed.IndexOf('>');
                if (angleBracketStart < 0 || angleBracketEnd < 0)
                    continue;

                var generics = trimmed[(angleBracketStart + 1)..angleBracketEnd];
                var parts = generics.Split(',', StringSplitOptions.TrimEntries);

                // Get the implementation type name (last generic arg, or only arg)
                var implTypeName = parts[^1];

                // Skip namespace-qualified external types (e.g., Bundles.ZipCacheCleanupWorker)
                if (implTypeName.Contains('.'))
                    continue;

                // Look up in plugin assemblies
                if (!pluginTypesByName.TryGetValue(implTypeName, out var matchingTypes))
                    continue; // type not in any plugin assembly — exempt (SDK, bannou-service, external)

                // Check if ANY matching type has [BannouHelperService]
                var hasAttribute = matchingTypes.Any(t =>
                    t.GetCustomAttribute<BannouHelperServiceAttribute>() != null);

                if (!hasAttribute)
                {
                    var assemblyNames = string.Join(", ", matchingTypes.Select(t => t.Assembly.GetName().Name));
                    violations.Add(
                        $"{relativePath}: services.Add*<{implTypeName}>() — " +
                        $"type lives in plugin assembly ({assemblyNames}) but has no [BannouHelperService] attribute");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} plugin type(s) registered in DI without [BannouHelperService] attribute. " +
            $"Plugin types that are DI-registered and injected should use the attribute for auto-discovery:\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
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

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Detects BackgroundService and IHostedService types in plugin assemblies that do not
    /// have [BannouHelperService] with RegistrationMode = HostedService or SingletonAndHostedService.
    /// These types should use the attribute for automatic registration instead of manual
    /// AddHostedService calls in Plugin.ConfigureServices.
    /// </summary>
    /// <remarks>
    /// Types that require conditional registration (backend mode branches, feature flags)
    /// must use <see cref="ConditionalServiceRegistration.AddConditionalHostedService{T}"/>
    /// in their Plugin.ConfigureServices. The structural test discovers these by scanning
    /// Plugin.cs source files for AddConditionalHostedService calls — no hardcoded exception list.
    /// </remarks>
    [Fact]
    public void HostedServices_ShouldUseBannouHelperServiceAttribute()
    {
        EnsureAssembliesLoaded();

        // Build set of type names registered via AddConditionalHostedService in Plugin.cs source.
        // These are approved self-registrations — the condition delegate is the justification.
        var conditionallyRegistered = new HashSet<string>();
        var pluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");
        if (Directory.Exists(pluginsDir))
        {
            foreach (var pluginFile in Directory.GetFiles(pluginsDir, "*Plugin.cs", SearchOption.AllDirectories))
            {
                foreach (var line in File.ReadAllLines(pluginFile))
                {
                    var trimmed = line.TrimStart();
                    if (!trimmed.Contains("AddConditionalHostedService<", StringComparison.Ordinal))
                        continue;

                    // Extract type name from AddConditionalHostedService<TypeName>(
                    var start = trimmed.IndexOf("AddConditionalHostedService<", StringComparison.Ordinal)
                        + "AddConditionalHostedService<".Length;
                    var end = trimmed.IndexOf('>', start);
                    if (end > start)
                    {
                        conditionallyRegistered.Add(trimmed[start..end]);
                    }
                }
            }
        }

        var hostedServiceInterface = typeof(Microsoft.Extensions.Hosting.IHostedService);
        var violations = new List<string>();

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null || !assemblyName.StartsWith("lib-", StringComparison.Ordinal))
                continue;

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
                // Skip interfaces, abstract classes, and non-IHostedService types
                if (type.IsInterface || type.IsAbstract || !hostedServiceInterface.IsAssignableFrom(type))
                    continue;

                // Skip types registered via AddConditionalHostedService (approved self-registration)
                if (conditionallyRegistered.Contains(type.Name))
                    continue;

                // Check for [BannouHelperService] with a hosted registration mode
                var attr = type.GetCustomAttribute<BannouHelperServiceAttribute>();
                if (attr == null)
                {
                    violations.Add(
                        $"{type.FullName} implements IHostedService but has no [BannouHelperService] attribute. " +
                        "Add [BannouHelperService(..., RegistrationMode = HelperRegistrationMode.HostedService)] " +
                        "or use AddConditionalHostedService<T> for conditional registration");
                    continue;
                }

                if (attr.RegistrationMode != HelperRegistrationMode.HostedService &&
                    attr.RegistrationMode != HelperRegistrationMode.SingletonAndHostedService)
                {
                    violations.Add(
                        $"{type.FullName} implements IHostedService but [BannouHelperService] has " +
                        $"RegistrationMode = {attr.RegistrationMode}. " +
                        "Use HelperRegistrationMode.HostedService or SingletonAndHostedService");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} IHostedService type(s) missing proper [BannouHelperService] attribute:\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Lists helper services (types with [BannouHelperService]) whose parent plugin
    /// has no corresponding helper configuration class (a type with [ServiceConfiguration]
    /// in the same assembly whose class name contains the helper type's name or a
    /// recognizable prefix). This is informational — not all helpers need dedicated
    /// configuration. Run this test directly to produce the checklist.
    /// </summary>
    [Fact]
    public void HelperServices_ShouldHaveConfigurationClasses()
    {
        SkipUnless.InformationalTest("Produces a checklist of helpers without dedicated configuration classes");
        EnsureAssembliesLoaded();

        var helpersWithoutConfig = new List<string>();

        foreach (var helperType in DiscoverAttributedTypes<BannouHelperServiceAttribute>())
        {
            var attr = helperType.GetCustomAttribute<BannouHelperServiceAttribute>()!;

            // Find all ServiceConfiguration types in the same assembly
            var configTypes = helperType.Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<ServiceConfigurationAttribute>() != null)
                .ToList();

            // Check if any config class name contains the helper type name (minus "Service" suffix)
            // e.g., TokenService -> AuthTokenConfiguration, EmailService -> AuthEmailConfiguration
            var helperBaseName = helperType.Name;
            if (helperBaseName.EndsWith("Service", StringComparison.Ordinal))
                helperBaseName = helperBaseName[..^"Service".Length];

            var hasConfig = configTypes.Any(ct =>
                ct.Name.Contains(helperBaseName, StringComparison.OrdinalIgnoreCase));

            if (!hasConfig)
            {
                var parentName = attr.ParentServiceType?.Name ?? "unknown";
                helpersWithoutConfig.Add($"{parentName} -> {helperType.Name} (no *{helperBaseName}Configuration found)");
            }
        }

        Assert.True(
            helpersWithoutConfig.Count == 0,
            $"{helpersWithoutConfig.Count} helper service(s) have no dedicated configuration class " +
            $"(consider adding x-helper-configurations to the service's config schema):\n" +
            string.Join("\n", helpersWithoutConfig.Select(h => $"  - {h}")));
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
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

    // ⛔ FROZEN — Do not modify without explicit user permission.
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

    // ⛔ FROZEN — Do not modify without explicit user permission.
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
    /// Publisher methods that are intentionally never called. These are narrow,
    /// structurally legitimate exceptions.
    /// </summary>
    private static readonly HashSet<string> IntentionallyUncalledPublishers = new(StringComparer.Ordinal)
    {
        // Immutable instance entities — never updated after creation
        "PublishLicenseBoardUpdatedAsync",
    };

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Validates that every generated Publish*Async extension method on the service's
    /// *EventPublisher class is called from somewhere in the plugin assembly.
    /// An uncalled method means a declared event topic is never published.
    /// Methods in <see cref="IntentionallyUncalledPublishers"/> are excluded.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_CallsAllGeneratedEventPublishers(Type serviceType)
    {
        SkipUnless.InformationalTest("Services with unimplemented event publishers need implementation work");
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

    // ⛔ FROZEN — Do not modify without explicit user permission.
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

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Validates that services with generated *ReferenceTracking.cs files actually call
    /// the registration methods from their Plugin.cs OnRunningAsync. CASCADE/DETACH references
    /// generate RegisterResourceCleanupCallbacksAsync; RESTRICT references generate
    /// RegisterResourceMigrateCallbacksAsync. If the plugin doesn't call them, callbacks
    /// are silently missing at runtime.
    /// </summary>
    [Fact]
    public void Services_WithResourceTracking_MustCallRegistration()
    {
        EnsureAssembliesLoaded();
        var failures = new List<string>();

        foreach (var serviceType in DiscoverAttributedTypes<BannouServiceAttribute>())
        {
            var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
            if (attr == null) continue;

            var serviceName = attr.Name;
            var pluginDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins", $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir)) continue;

            var generatedDir = Path.Combine(pluginDir, "Generated");
            if (!Directory.Exists(generatedDir)) continue;

            var trackingFiles = Directory.GetFiles(generatedDir, "*ReferenceTracking.cs");
            if (trackingFiles.Length == 0) continue;

            // Read the generated file to determine which registration methods exist
            var trackingContent = File.ReadAllText(trackingFiles[0]);

            // Match actual method definitions, not comment references
            var methodsToCheck = new List<string>();
            if (trackingContent.Contains("Task<bool> RegisterResourceCleanupCallbacksAsync("))
                methodsToCheck.Add("RegisterResourceCleanupCallbacksAsync");
            if (trackingContent.Contains("Task<bool> RegisterResourceMigrateCallbacksAsync("))
                methodsToCheck.Add("RegisterResourceMigrateCallbacksAsync");

            if (methodsToCheck.Count == 0) continue;

            // Read all Plugin.cs files to check for registration calls
            var pluginFiles = Directory.GetFiles(pluginDir, "*Plugin.cs", SearchOption.TopDirectoryOnly);
            var pluginContent = string.Join("\n", pluginFiles.Select(File.ReadAllText));

            foreach (var method in methodsToCheck)
            {
                if (!pluginContent.Contains(method))
                {
                    failures.Add(
                        $"{serviceType.Name} (lib-{serviceName}): generated *ReferenceTracking.cs " +
                        $"defines {method}() but *Plugin.cs does not call it");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Services with x-references must call their generated registration methods " +
            $"in Plugin OnRunningAsync (generated but not wired up):\n" +
            string.Join("\n", failures.Select(f => $"  - {f}")));
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Validates that services with generated *CompressionCallbacks.cs actually call
    /// RegisterAsync() from their Plugin.cs OnRunningAsync. The file is generated from
    /// x-compression-callback in the API schema, but if the plugin doesn't call it,
    /// compression callbacks are silently missing at runtime.
    /// </summary>
    [Fact]
    public void Services_WithCompressionCallbacks_MustCallRegistration()
    {
        EnsureAssembliesLoaded();
        var failures = new List<string>();

        foreach (var serviceType in DiscoverAttributedTypes<BannouServiceAttribute>())
        {
            var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
            if (attr == null) continue;

            var serviceName = attr.Name;
            var pluginDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins", $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir)) continue;

            var generatedDir = Path.Combine(pluginDir, "Generated");
            if (!Directory.Exists(generatedDir)) continue;

            var compressionFiles = Directory.GetFiles(generatedDir, "*CompressionCallbacks.cs");
            if (compressionFiles.Length == 0) continue;

            // Extract the class name from the file for more precise matching
            var callbackClassName = Path.GetFileNameWithoutExtension(compressionFiles[0]);

            var pluginFiles = Directory.GetFiles(pluginDir, "*Plugin.cs", SearchOption.TopDirectoryOnly);
            var callFound = false;

            foreach (var pluginFile in pluginFiles)
            {
                var content = File.ReadAllText(pluginFile);
                // Check for either "CompressionCallbacks.RegisterAsync" or the specific class name
                if (content.Contains("CompressionCallbacks.RegisterAsync") ||
                    content.Contains($"{callbackClassName}.RegisterAsync"))
                {
                    callFound = true;
                    break;
                }
            }

            if (!callFound)
            {
                failures.Add(
                    $"{serviceType.Name} (lib-{serviceName}): has generated {callbackClassName}.cs " +
                    $"but *Plugin.cs does not call RegisterAsync()");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Services with x-compression-callback must call CompressionCallbacks.RegisterAsync() " +
            $"in their Plugin OnRunningAsync (generated but not wired up):\n" +
            string.Join("\n", failures.Select(f => $"  - {f}")));
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Validates that services with non-empty x-event-subscriptions in their event schema:
    /// 1. Have a RegisterEventConsumers method in *Service.Events.cs
    /// 2. Call RegisterEventConsumers from the *Service.cs constructor
    /// 3. Register a handler for EACH subscribed event type
    /// If a service declares event subscriptions in the schema but never wires them up,
    /// events are silently lost at runtime (IMPLEMENTATION TENETS T3).
    /// </summary>
    ///
    // ⛔ FROZEN — Only the human adds entries. Agents MUST NOT modify this list.
    // Contains "service-name:EventTypeName" entries for subscriptions handled via
    // alternative mechanisms (e.g., direct IMessageSubscriber in a helper service)
    // rather than IEventConsumer in *Service.Events.cs.
    private static readonly HashSet<string> EventSubscriptionHandlerExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        // mesh:MeshCircuitStateChangedEvent — handled by DistributedCircuitBreaker via
        // direct IMessageSubscriber subscription in MeshInvocationClient, not IEventConsumer.
        // See docs/maps/MESH.md § HandleCircuitStateChanged.
        "mesh:MeshCircuitStateChangedEvent",
    };

    [Fact]
    public void Services_WithEventSubscriptions_MustRegisterConsumers()
    {
        EnsureAssembliesLoaded();
        var schemasDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "schemas");
        var failures = new List<string>();

        foreach (var file in Directory.EnumerateFiles(schemasDir, "*-events.yaml"))
        {
            var fileName = Path.GetFileName(file);
            // Skip Generated/ and common-events.yaml
            if (fileName == "common-events.yaml")
                continue;

            var lines = File.ReadAllLines(file);

            // Parse x-event-subscriptions entries (topic + event type pairs)
            var subscriptions = ParseSubscriptionEntries(lines);
            if (subscriptions.Count == 0)
                continue;

            // Extract service name: "auth-service-events.yaml" -> "auth"
            var serviceName = fileName.Replace("-service-events.yaml", "").Replace("-events.yaml", "");
            var pluginDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins", $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir))
                continue;

            // Find *Service.Events.cs (dot convention per FOUNDATION TENETS T6)
            var eventsFiles = Directory.GetFiles(pluginDir, "*Service.Events.cs", SearchOption.TopDirectoryOnly);
            string? eventsContent = null;
            foreach (var eventsFile in eventsFiles)
            {
                var content = File.ReadAllText(eventsFile);
                if (content.Contains("RegisterEventConsumers", StringComparison.Ordinal))
                {
                    eventsContent = content;
                    break;
                }
            }

            if (eventsContent == null)
            {
                failures.Add(
                    $"lib-{serviceName}: has x-event-subscriptions in {fileName} " +
                    $"but *Service.Events.cs does not define RegisterEventConsumers()");
                continue;
            }

            // Verify *Service.cs constructor calls RegisterEventConsumers
            var serviceFiles = Directory.GetFiles(pluginDir, "*Service.cs", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).Contains("Events", StringComparison.Ordinal) &&
                            !Path.GetFileName(f).Contains("Models", StringComparison.Ordinal) &&
                            !Path.GetFileName(f).Contains("Plugin", StringComparison.Ordinal) &&
                            !Path.GetFileName(f).Contains("Helpers", StringComparison.Ordinal))
                .ToList();

            var hasConstructorCall = false;
            foreach (var serviceFile in serviceFiles)
            {
                var content = File.ReadAllText(serviceFile);
                if (content.Contains("RegisterEventConsumers", StringComparison.Ordinal))
                {
                    hasConstructorCall = true;
                    break;
                }
            }

            if (!hasConstructorCall)
            {
                failures.Add(
                    $"lib-{serviceName}: has RegisterEventConsumers in *Service.Events.cs " +
                    $"but *Service.cs constructor does not call it");
            }

            // Verify each subscribed event type has a RegisterHandler call
            foreach (var (topic, eventType) in subscriptions)
            {
                // Skip events handled via alternative mechanisms (direct IMessageSubscriber, etc.)
                var exclusionKey = $"{serviceName}:{eventType}";
                if (EventSubscriptionHandlerExclusions.Contains(exclusionKey))
                    continue;

                if (!eventsContent.Contains(eventType, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"lib-{serviceName}: x-event-subscriptions declares event '{eventType}' " +
                        $"(topic: {topic}) but *Service.Events.cs has no RegisterHandler for it");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Services with x-event-subscriptions must define and call RegisterEventConsumers, " +
            $"with a RegisterHandler for each subscribed event type " +
            $"(IMPLEMENTATION TENETS T3 — event handlers silently missing):\n" +
            string.Join("\n", failures.Select(f => $"  - {f}")));
    }

    /// <summary>
    /// Parses x-event-subscriptions entries from event schema lines.
    /// Returns a list of (topic, eventType) pairs.
    /// </summary>
    private static List<(string Topic, string EventType)> ParseSubscriptionEntries(string[] lines)
    {
        var result = new List<(string, string)>();
        var inSubscriptions = false;
        string? currentTopic = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("x-event-subscriptions:", StringComparison.Ordinal))
            {
                if (trimmed.Contains("[]", StringComparison.Ordinal))
                    break;
                inSubscriptions = true;
                continue;
            }

            if (!inSubscriptions)
                continue;

            // Exit subscriptions block at the next top-level key
            if (trimmed.Length > 0 && !char.IsWhiteSpace(lines[i][0])
                && !trimmed.StartsWith("-", StringComparison.Ordinal)
                && !trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                break;
            }

            // Exit at sibling x-* extension attributes at the same indentation level
            // (prevents bleeding from x-event-subscriptions into x-event-publications)
            if (trimmed.StartsWith("x-", StringComparison.Ordinal) && trimmed.Contains(':', StringComparison.Ordinal))
            {
                break;
            }

            if (trimmed.StartsWith("- topic:", StringComparison.Ordinal))
            {
                currentTopic = trimmed["- topic:".Length..].Trim().Trim('"', '\'');
            }
            else if (trimmed.StartsWith("topic:", StringComparison.Ordinal))
            {
                currentTopic = trimmed["topic:".Length..].Trim().Trim('"', '\'');
            }
            else if (trimmed.StartsWith("event:", StringComparison.Ordinal) && currentTopic != null)
            {
                var eventType = trimmed["event:".Length..].Trim().Trim('"', '\'');
                if (!string.IsNullOrEmpty(eventType))
                {
                    result.Add((currentTopic, eventType));
                }
            }
        }

        return result;
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Validates that no manual (non-Generated) plugin source file calls
    /// TryPublishAsync with an inline string literal topic. Generated event publishers
    /// (*EventPublisher.cs) and topic constants (*PublishedTopics.cs) exist for every
    /// published event — services must use those instead of raw topic strings.
    /// Catches the pattern: TryPublishAsync("topic.name", ...) in non-Generated code.
    /// Does NOT flag: TryPublishErrorAsync (different method, takes service name not topic),
    /// interpolated strings ($"..."), or const/variable references.
    /// </summary>
    [Fact]
    public void Services_MustNotUseTryPublishAsyncWithStringLiterals()
    {
        EnsureAssembliesLoaded();
        var violations = new List<string>();
        var pluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");

        foreach (var serviceType in DiscoverAttributedTypes<BannouServiceAttribute>())
        {
            var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
            if (attr == null) continue;

            var serviceName = attr.Name;
            var pluginDir = Path.Combine(pluginsDir, $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir)) continue;

            // Scan all .cs files in the plugin directory, excluding Generated/ and test projects
            var sourceFiles = Directory.EnumerateFiles(pluginDir, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var relativePath = Path.GetRelativePath(pluginDir, f);
                    return !relativePath.StartsWith("Generated", StringComparison.OrdinalIgnoreCase) &&
                            !relativePath.Contains("bin", StringComparison.OrdinalIgnoreCase) &&
                            !relativePath.Contains("obj", StringComparison.OrdinalIgnoreCase);
                });

            foreach (var sourceFile in sourceFiles)
            {
                var lines = File.ReadAllLines(sourceFile);
                var relPath = Path.GetRelativePath(TestAssemblyDiscovery.RepoRoot, sourceFile);

                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    // Match TryPublishAsync(" but NOT TryPublishErrorAsync("
                    // The pattern: TryPublishAsync followed by ( then optional whitespace then "
                    if (line.Contains("TryPublishAsync(\"", StringComparison.Ordinal) &&
                        !line.Contains("TryPublishErrorAsync", StringComparison.Ordinal))
                    {
                        var trimmed = line.Trim();
                        violations.Add($"{relPath}:{i + 1}: {trimmed}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} TryPublishAsync call(s) with inline string literal topics. " +
            $"Use generated Publish*Async extension methods or *PublishedTopics constants instead " +
            $"(FOUNDATION TENETS — Event-Driven Architecture):\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
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

    // ⛔ FROZEN — Do not modify without explicit user permission.
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

    // ⛔ FROZEN — Do not modify without explicit user permission.
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

    // ⛔ FROZEN — Do not modify without explicit user permission.
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
    /// Validates that no plugin assembly uses Enum.Parse or Enum.TryParse.
    /// The presence of Enum.Parse indicates a model definition is wrong — the field
    /// should use a typed enum instead of string (IMPLEMENTATION TENETS T25).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_NoEnumParseUsage(Type serviceType)
    {
        var assemblyPath = serviceType.Assembly.Location;
        if (string.IsNullOrEmpty(assemblyPath))
            return;

        string[] methods = ["Parse", "TryParse"];
        var found = AssemblyMetadataScanner.GetReferencedMethods(
            assemblyPath, "Enum", methods);

        Assert.True(
            found.Count == 0,
            $"{serviceType.Name}: calls Enum.{string.Join("/", found)} " +
            $"— model definition is wrong, use typed enum instead (IMPLEMENTATION TENETS)");
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    //
    // DESIGN NOTE: Why source-code scanning instead of IL-level scanning?
    // ────────────────────────────────────────────────────────────────────
    // The original implementation used AssemblyMetadataScanner to detect TaskAwaiter.GetResult(),
    // Task.Wait(), and Task<T>.Result references in compiled assembly IL (MemberRef table scanning).
    // This approach was fundamentally flawed: the C# compiler's async state machine generates
    // TaskAwaiter.GetResult() calls in IL as part of NORMAL await expressions. Every compiled
    // `await someTask` produces a state machine whose MoveNext() method calls
    // TaskAwaiter.GetResult() to unwrap the task result after completion. This is correct,
    // non-blocking compiler output — the state machine only calls GetResult() after the task
    // has completed and the continuation has been scheduled.
    //
    // Result: ALL 45+ service assemblies failed the test because every assembly with any
    // async/await code contains TaskAwaiter.GetResult() MemberRef entries. The IL scanner
    // cannot distinguish between:
    //   1. Source code `.GetAwaiter().GetResult()` (blocking, threadpool starvation — bad)
    //   2. Compiler-generated `TaskAwaiter.GetResult()` in async state machines (correct)
    //
    // Lesson: IL-level method reference scanning is appropriate for APIs that are NEVER
    // compiler-generated (e.g., Enum.Parse, JsonSerializer.Serialize). It is NOT appropriate
    // for APIs that the compiler itself emits as part of language feature compilation (async/await,
    // LINQ expression trees, pattern matching, etc.). For those, source-code text scanning is
    // the only reliable approach.
    //
    /// <summary>
    /// Validates that no plugin source code uses blocking async patterns:
    /// .GetAwaiter().GetResult(), Task.Wait/WaitAll/WaitAny(), .Result on awaited tasks,
    /// or Task/ValueTask.FromResult without async. These cause threadpool starvation and
    /// potential deadlocks — use await instead (IMPLEMENTATION TENETS).
    /// Scans source files (not IL) to avoid false positives from compiler-generated
    /// async state machine code.
    /// </summary>
    [Fact]
    public void Services_MustNotUseBlockingAsyncPatterns()
    {
        EnsureAssembliesLoaded();
        var violations = new List<string>();
        var pluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");

        // Patterns that are ALWAYS blocking violations in source code:
        // - .GetAwaiter().GetResult() — sync-over-async, threadpool starvation
        // - .Wait() / .WaitAll() / .WaitAny() on Task — same problem
        // - .Result on an awaited expression — same problem
        //
        // Note: .Wait() on SemaphoreSlim is a different API (synchronous lock, not Task blocking).
        // We detect the Task-specific pattern by looking for the .GetAwaiter().GetResult() chain
        // and Task.Wait/WaitAll/WaitAny specifically.

        string[] blockingPatterns =
        [
            ".GetAwaiter().GetResult()",
            "Task.WaitAll(",
            "Task.WaitAny(",
        ];

        foreach (var serviceType in DiscoverAttributedTypes<BannouServiceAttribute>())
        {
            var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
            if (attr == null) continue;

            var serviceName = attr.Name;
            var pluginDir = Path.Combine(pluginsDir, $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir)) continue;

            var sourceFiles = Directory.EnumerateFiles(pluginDir, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var relativePath = Path.GetRelativePath(pluginDir, f);
                    return !relativePath.StartsWith("Generated", StringComparison.OrdinalIgnoreCase) &&
                            !relativePath.Contains("bin", StringComparison.OrdinalIgnoreCase) &&
                            !relativePath.Contains("obj", StringComparison.OrdinalIgnoreCase);
                });

            foreach (var sourceFile in sourceFiles)
            {
                var lines = File.ReadAllLines(sourceFile);
                var relPath = Path.GetRelativePath(TestAssemblyDiscovery.RepoRoot, sourceFile);

                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];

                    // Skip comment-only lines
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal))
                        continue;

                    foreach (var pattern in blockingPatterns)
                    {
                        if (line.Contains(pattern, StringComparison.Ordinal))
                        {
                            violations.Add($"{relPath}:{i + 1}: {trimmed}");
                            break; // One violation per line is enough
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} blocking async pattern(s) in plugin source code. " +
            $"Use await instead — blocking calls cause threadpool starvation and deadlocks " +
            $"(IMPLEMENTATION TENETS):\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
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
            "AssertSwitchCoversAllValues", "AssertSourceCoveredByTarget",
            "AssertStringToEnumCoverage"
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

    // ⛔ FROZEN — Do not modify without explicit user permission.
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
    /// service names that have at least one deprecated entity. Service names are derived
    /// by stripping <c>-service-events</c> from the file name so they match the
    /// <see cref="BannouServiceAttribute.Name"/> used by <see cref="FindServiceTypeByName"/>.
    /// </summary>
    private static HashSet<string> DiscoverServicesWithDeprecation(string schemasDir)
    {
        var services = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(schemasDir, "*-service-events.yaml"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!fileName.EndsWith("-service-events", StringComparison.Ordinal))
                continue;

            // Strip "-service-events" so the remainder matches BannouServiceAttribute.Name
            // (e.g. "genesis-service-events.yaml" -> "genesis", matching [BannouService("genesis", ...)]).
            var serviceName = fileName[..^"-service-events".Length];

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

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Validates that every service implementing <see cref="IAccountDeletionCleanupRequired"/>
    /// has a HandleAccountDeletedAsync method. Services storing account-owned data must handle
    /// account.deleted events per FOUNDATION TENETS (T28 Account Deletion Cleanup Obligation).
    /// </summary>
    [Fact]
    public void Services_WithAccountCleanup_MustHaveHandler()
    {
        EnsureAssembliesLoaded();
        var failures = new List<string>();

        foreach (var serviceType in DiscoverAttributedTypes<BannouServiceAttribute>())
        {
            if (!typeof(IAccountDeletionCleanupRequired).IsAssignableFrom(serviceType))
                continue;

            var hasHandler = serviceType.GetMethod("HandleAccountDeletedAsync",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;

            if (!hasHandler)
            {
                failures.Add(
                    $"{serviceType.Name}: implements IAccountDeletionCleanupRequired " +
                    $"but has no HandleAccountDeletedAsync method");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Services implementing IAccountDeletionCleanupRequired must have " +
            $"HandleAccountDeletedAsync (per FOUNDATION TENETS T28):\n" +
            string.Join("\n", failures.Select(f => $"  - {f}")));
    }

    /// <summary>
    /// Discovers services that subscribe to account.deleted events by scanning
    /// event schema files for <c>x-event-subscriptions</c> with topic <c>account.deleted</c>.
    /// Services that subscribe but don't implement <see cref="IAccountDeletionCleanupRequired"/>
    /// are flagged for interface adoption.
    /// </summary>
    [Fact]
    public void Services_SubscribingToAccountDeleted_MustImplementInterface()
    {
        EnsureAssembliesLoaded();

        var schemasDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "schemas");
        if (!Directory.Exists(schemasDir))
            return;

        var failures = new List<string>();
        var subscribingServices = DiscoverServicesSubscribingToAccountDeleted(schemasDir);

        foreach (var serviceName in subscribingServices)
        {
            var serviceType = FindServiceTypeByName(serviceName);
            if (serviceType == null)
            {
                failures.Add($"{serviceName}: subscribes to account.deleted but no [BannouService] class found");
                continue;
            }

            if (!typeof(IAccountDeletionCleanupRequired).IsAssignableFrom(serviceType))
            {
                failures.Add(
                    $"{serviceType.Name} (service: {serviceName}): subscribes to account.deleted in events schema " +
                    $"but does not implement IAccountDeletionCleanupRequired");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Services subscribing to account.deleted must implement IAccountDeletionCleanupRequired " +
            $"(per FOUNDATION TENETS T28):\n" +
            string.Join("\n", failures.Select(f => $"  - {f}")));
    }

    /// <summary>
    /// Scans event schema files for <c>x-event-subscriptions</c> entries with
    /// topic <c>account.deleted</c> and returns the set of subscribing service names.
    /// </summary>
    private static HashSet<string> DiscoverServicesSubscribingToAccountDeleted(string schemasDir)
    {
        var services = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(schemasDir, "*-events.yaml"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!fileName.EndsWith("-events", StringComparison.Ordinal))
                continue;

            var serviceName = fileName[..^"-events".Length];
            var inSubscriptions = false;

            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("x-event-subscriptions:", StringComparison.Ordinal))
                {
                    inSubscriptions = true;
                    continue;
                }

                // Exit subscriptions block when we hit another top-level key
                if (inSubscriptions && !trimmed.StartsWith("-", StringComparison.Ordinal)
                    && !trimmed.StartsWith("topic:", StringComparison.Ordinal)
                    && !trimmed.StartsWith("event:", StringComparison.Ordinal)
                    && !trimmed.StartsWith("handler:", StringComparison.Ordinal)
                    && trimmed.Length > 0 && !char.IsWhiteSpace(line[0]))
                {
                    inSubscriptions = false;
                }

                // Exit at sibling x-* extension attributes at the same indentation level
                if (inSubscriptions && trimmed.StartsWith("x-", StringComparison.Ordinal)
                    && trimmed.Contains(':', StringComparison.Ordinal))
                {
                    inSubscriptions = false;
                }

                if (inSubscriptions && trimmed.StartsWith("topic: account.deleted", StringComparison.Ordinal))
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

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Validates that all plugin directories (plugins/lib-*) are referenced by structural-tests.csproj.
    /// Missing references mean the plugin's services are invisible to all structural validation —
    /// constructor checks, hierarchy checks, key builders, telemetry, etc. all silently skip.
    /// Excludes lib-testing (test infrastructure, not a service plugin) and lib-*.tests directories.
    /// </summary>
    [Fact]
    public void Plugins_AreReferencedByStructuralTests()
    {
        var pluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");
        var csprojPath = Path.Combine(TestAssemblyDiscovery.RepoRoot, "structural-tests", "structural-tests.csproj");

        Assert.True(File.Exists(csprojPath), "structural-tests.csproj not found");

        var csprojContent = File.ReadAllText(csprojPath);

        var pluginDirs = Directory.GetDirectories(pluginsDir, "lib-*")
            .Select(Path.GetFileName)
            .Where(d => d != null
                && !d.EndsWith(".tests", StringComparison.Ordinal)
                && d != "lib-testing")
            .OrderBy(d => d)
            .ToList();

        var missing = pluginDirs
            .Where(d => !csprojContent.Contains($"{d}{Path.DirectorySeparatorChar}{d}.csproj", StringComparison.Ordinal)
                    && !csprojContent.Contains($"{d}/{d}.csproj", StringComparison.Ordinal)
                    && !csprojContent.Contains($"{d}\\{d}.csproj", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"structural-tests.csproj must reference all plugin projects. " +
            $"{missing.Count} plugin(s) are invisible to structural validation:\n" +
            string.Join("\n", missing.Select(d => $"  - {d}")));
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Validates that all concrete plugin classes extend StandardServicePlugin&lt;T&gt;
    /// rather than BaseBannouPlugin directly. StandardServicePlugin provides standardized
    /// lifecycle management (scoped service resolution, IBannouService integration, proper
    /// start/running/shutdown phases). Plugins extending BaseBannouPlugin directly must
    /// reimplement this lifecycle manually, which is error-prone and inconsistent.
    /// </summary>
    [Fact]
    public void Plugins_ExtendStandardServicePlugin()
    {
        EnsureAssembliesLoaded();

        var violations = new List<string>();
        var standardPluginOpenType = typeof(StandardServicePlugin<>);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null || !assemblyName.StartsWith("lib-", StringComparison.Ordinal))
                continue;

            // Skip test assemblies
            if (assemblyName.EndsWith(".tests", StringComparison.Ordinal))
                continue;

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
                if (type.IsAbstract || !type.IsClass)
                    continue;

                if (!typeof(BaseBannouPlugin).IsAssignableFrom(type))
                    continue;

                if (!ExtendsGenericBaseType(type, standardPluginOpenType))
                {
                    violations.Add(
                        $"{assemblyName}: {type.Name} extends BaseBannouPlugin directly " +
                        $"— should extend StandardServicePlugin<T>");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} plugin(s) extend BaseBannouPlugin directly instead of " +
            $"StandardServicePlugin<T>. StandardServicePlugin provides standardized lifecycle " +
            $"management — migrate these plugins:\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    /// <summary>
    /// Checks whether a type extends a specific open generic base type at any depth
    /// in the inheritance chain. For example, checks if FooPlugin extends
    /// StandardServicePlugin&lt;&gt; (regardless of the type parameter).
    /// </summary>
    private static bool ExtendsGenericBaseType(Type type, Type openGenericBase)
    {
        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == openGenericBase)
                return true;
            baseType = baseType.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Ensures all plugin assemblies are loaded into the AppDomain.
    /// Project references guarantee compilation but not loading — the CLR
    /// lazy-loads assemblies on first type access. This method scans the
    /// output directory for lib-*.dll files and loads them dynamically,
    /// eliminating the need for a manually-maintained type anchor list.
    /// </summary>
    private static void EnsureAssembliesLoaded()
    {
        var outputDir = Path.GetDirectoryName(typeof(StructuralTests).Assembly.Location);
        if (outputDir == null)
            return;

        foreach (var dllPath in Directory.GetFiles(outputDir, "lib-*.dll"))
        {
            var fileName = Path.GetFileNameWithoutExtension(dllPath);

            // Skip test assemblies
            if (fileName.EndsWith(".tests", StringComparison.Ordinal))
                continue;

            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(dllPath);
                var existing = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);

                if (existing == null)
                    Assembly.LoadFrom(dllPath);
            }
            catch
            {
                // Skip assemblies that fail to load (safe failure mode —
                // those plugins simply won't be validated by structural tests)
            }
        }
    }

    /// <summary>
    /// Informational test that inventories all published event topics across the system and
    /// identifies which ones have no subscriber (x-event-subscriptions) in any service.
    /// This is NOT a violation — FOUNDATION TENETS explicitly state "publish events even
    /// without current consumers." This test exists to provide visibility and replace
    /// scattered "no consumers" tickets with a single trackable checklist.
    /// </summary>
    [Fact]
    public void Events_PublishedTopicsWithoutSubscribers()
    {
        SkipUnless.InformationalTest("Produces a checklist of published events with no subscribers");
        var schemasDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "schemas");
        if (!Directory.Exists(schemasDir))
            return;

        // Phase 1: Collect all published topics (topic -> publishing service(s))
        var publishedTopics = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // Phase 2: Collect all subscribed topics (topic -> subscribing service(s))
        var subscribedTopics = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(schemasDir, "*-events.yaml"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!fileName.EndsWith("-events", StringComparison.Ordinal))
                continue;

            // Skip Generated/ subdirectory files
            if (file.Contains(Path.Combine("schemas", "Generated"), StringComparison.Ordinal))
                continue;

            var serviceName = fileName[..^"-events".Length];
            if (serviceName == "common")
                continue;

            var lines = File.ReadAllLines(file);
            ParseEventPublications(lines, serviceName, publishedTopics);
            ParseEventSubscriptions(lines, serviceName, subscribedTopics);
        }

        // Also scan Generated/*-lifecycle-events.yaml for auto-generated lifecycle publications
        var generatedDir = Path.Combine(schemasDir, "Generated");
        if (Directory.Exists(generatedDir))
        {
            foreach (var file in Directory.GetFiles(generatedDir, "*-lifecycle-events.yaml"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                // Format: {service}-lifecycle-events.yaml
                var serviceName = fileName.Replace("-lifecycle-events", "");
                var lines = File.ReadAllLines(file);
                ParseEventPublications(lines, serviceName, publishedTopics);
            }
        }

        // Phase 3: Find published topics with no subscribers
        var unconsumed = new List<string>();
        foreach (var (topic, publishers) in publishedTopics.OrderBy(kv => kv.Key))
        {
            if (!subscribedTopics.ContainsKey(topic))
            {
                unconsumed.Add($"{topic} (published by: {string.Join(", ", publishers)})");
            }
        }

        var consumed = publishedTopics.Count - unconsumed.Count;

        Assert.True(
            unconsumed.Count == 0,
            $"Event consumer coverage: {consumed}/{publishedTopics.Count} published topics have subscribers. " +
            $"{unconsumed.Count} published topic(s) have no subscribers in any service's x-event-subscriptions:\n" +
            string.Join("\n", unconsumed.Select(u => $"  - {u}")));
    }

    /// <summary>
    /// Parses x-event-publications from event schema lines and adds to the dictionary.
    /// </summary>
    private static void ParseEventPublications(
        string[] lines, string serviceName, Dictionary<string, List<string>> publications)
    {
        var inPublications = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("x-event-publications:", StringComparison.Ordinal))
            {
                // Check for empty: "x-event-publications: []"
                if (trimmed.Contains("[]", StringComparison.Ordinal))
                    continue;
                inPublications = true;
                continue;
            }

            // Exit publications block at the next top-level key (no indent or different section)
            if (inPublications && trimmed.Length > 0 && !char.IsWhiteSpace(lines[i][0])
                && !trimmed.StartsWith("-", StringComparison.Ordinal)
                && !trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                inPublications = false;
            }

            // Exit at sibling x-* extension attributes at the same indentation level
            // (prevents bleeding from x-event-publications into x-event-subscriptions)
            if (inPublications && trimmed.StartsWith("x-", StringComparison.Ordinal)
                && trimmed.Contains(':', StringComparison.Ordinal))
            {
                inPublications = false;
            }

            if (inPublications && trimmed.StartsWith("- topic:", StringComparison.Ordinal))
            {
                var topic = trimmed["- topic:".Length..].Trim();
                // Strip quotes if present
                if (topic.StartsWith('"') && topic.EndsWith('"'))
                    topic = topic[1..^1];
                if (topic.StartsWith('\'') && topic.EndsWith('\''))
                    topic = topic[1..^1];

                if (!publications.TryGetValue(topic, out var list))
                {
                    list = new List<string>();
                    publications[topic] = list;
                }
                list.Add(serviceName);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Telemetry Span Placement Validation
    // Per IMPLEMENTATION TENETS T30: primary interface methods MUST NOT have spans
    // (generated controller wraps them); helper methods MUST have spans.
    // ═══════════════════════════════════════════════════════════════════════════

    // ⛔ FROZEN — Only the human adds entries. Agents MUST NOT modify this list.
    // Contains "service-name" entries for services whose primary {Service}Service.cs file
    // still contains StartActivity calls because internal helpers have not yet been migrated
    // to a separate {Service}Service.Helpers.cs partial class file.
    // As helpers are migrated out, entries are removed from this list.
    private static readonly HashSet<string> TelemetrySpanPrimaryFileExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Populated by human only — agents must not add entries
    };

    // ⛔ FROZEN — Only the human adds entries. Agents MUST NOT modify this list.
    // Contains "service-name:FileName.cs" entries for specific helper files that
    // legitimately contain async methods without StartActivity spans.
    // Valid reasons: pure data mapping, simple delegation, test infrastructure.
    private static readonly HashSet<string> TelemetrySpanHelperFileExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Populated by human only — agents must not add entries
    };

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Validates that primary service implementation files ({Service}Service.cs) do NOT
    /// call ITelemetryProvider.StartActivity. Primary interface methods are already wrapped
    /// by the generated controller's catch-all boundary with telemetry instrumentation.
    /// Adding spans in the service method would double-instrument these endpoints.
    /// </summary>
    /// <remarks>
    /// This test incentivizes the {Service}Service.Helpers.cs pattern: internal helper methods
    /// that DO need spans should live in a separate partial class file, keeping the main
    /// service file clean of telemetry calls. Services in the exclusion list have not yet
    /// completed the helpers migration.
    /// </remarks>
    [Fact]
    public void Services_PrimaryFile_DoesNotCallStartActivity()
    {
        EnsureAssembliesLoaded();
        var failures = new List<string>();

        foreach (var serviceType in DiscoverAttributedTypes<BannouServiceAttribute>())
        {
            var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
            if (attr == null) continue;

            var serviceName = attr.Name;

            // Skip services in the exclusion list (helpers not yet migrated)
            if (TelemetrySpanPrimaryFileExclusions.Contains(serviceName))
                continue;

            var pluginDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins", $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir)) continue;

            // Find the EXACT primary service file by deriving its name from the attribute.
            // "faction" → "FactionService.cs", "character-lifecycle" → "CharacterLifecycleService.cs"
            // This avoids false matches on BackgroundService files like ContractExpirationService.cs
            // or helper services like RarityCalculationService.cs.
            var primaryFileName = ServiceNameToPascalCase(serviceName) + "Service.cs";
            var primaryFilePath = Path.Combine(pluginDir, primaryFileName);

            if (!File.Exists(primaryFilePath))
                continue;

            var content = File.ReadAllText(primaryFilePath);
            if (content.Contains("StartActivity", StringComparison.Ordinal))
            {
                failures.Add(
                    $"lib-{serviceName}/{primaryFileName}: contains StartActivity call(s). " +
                    $"Primary interface methods are instrumented by the generated controller — " +
                    $"move internal helpers to {primaryFileName.Replace(".cs", ".Helpers.cs")} " +
                    $"(per IMPLEMENTATION TENETS T30)");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Primary service files must not call StartActivity " +
            $"(generated controller already instruments interface methods):\n" +
            string.Join("\n", failures));
    }

    // ⛔ FROZEN — Do not modify without explicit user permission.
    /// <summary>
    /// Validates that non-primary plugin source files containing async methods also
    /// contain at least one ITelemetryProvider.StartActivity call. Helper methods,
    /// event handlers, providers, and workers all require telemetry span instrumentation
    /// per IMPLEMENTATION TENETS T30.
    /// </summary>
    /// <remarks>
    /// Scans all non-generated .cs files in each plugin directory, excluding the primary
    /// service file, plugin file, and models file. If a file contains any async method
    /// signature (async Task or async ValueTask), it must also contain StartActivity.
    /// Files in the exclusion list are exempt (e.g., pure data mapping helpers).
    /// </remarks>
    [Fact]
    public void Services_HelperFiles_ContainTelemetryInstrumentation()
    {
        EnsureAssembliesLoaded();
        var failures = new List<string>();

        foreach (var serviceType in DiscoverAttributedTypes<BannouServiceAttribute>())
        {
            var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
            if (attr == null) continue;

            var serviceName = attr.Name;
            var pluginDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins", $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir)) continue;

            // Find all non-generated .cs files
            var allSourceFiles = Directory.GetFiles(pluginDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.Combine("Generated", ""), StringComparison.Ordinal) &&
                            !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                            !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .ToList();

            foreach (var sourceFile in allSourceFiles)
            {
                var fileName = Path.GetFileName(sourceFile);

                // Skip files that are exempt from span requirements:
                // - Primary service file (validated by the companion test above)
                // - Plugin registration file (infrastructure, not business logic)
                // - Models file (no async methods, pure data)
                // - AssemblyInfo.cs, GlobalUsings.cs (infrastructure)
                var primaryFileName = ServiceNameToPascalCase(serviceName) + "Service.cs";
                if (fileName.Equals(primaryFileName, StringComparison.Ordinal) ||
                    fileName.EndsWith("Plugin.cs", StringComparison.Ordinal) ||
                    fileName.EndsWith("Models.cs", StringComparison.Ordinal) ||
                    fileName.Equals("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("GlobalUsings.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Check exclusion list
                var exclusionKey = $"{serviceName}:{fileName}";
                if (TelemetrySpanHelperFileExclusions.Contains(exclusionKey))
                    continue;

                var content = File.ReadAllText(sourceFile);

                // Only check files that actually contain async methods
                if (!content.Contains("async Task", StringComparison.Ordinal) &&
                    !content.Contains("async ValueTask", StringComparison.Ordinal))
                {
                    continue;
                }

                // File has async methods — it should have at least one StartActivity call
                if (!content.Contains("StartActivity", StringComparison.Ordinal))
                {
                    var relativePath = Path.GetRelativePath(
                        Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins"), sourceFile);
                    failures.Add(
                        $"{relativePath}: contains async methods but no StartActivity call " +
                        $"(per IMPLEMENTATION TENETS T30 — all async helper methods need telemetry spans)");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Helper files with async methods must contain telemetry instrumentation:\n" +
            string.Join("\n", failures));
    }

    /// <summary>
    /// Converts a kebab-case service name to PascalCase.
    /// "faction" → "Faction", "character-lifecycle" → "CharacterLifecycle",
    /// "game-session" → "GameSession".
    /// </summary>
    private static string ServiceNameToPascalCase(string serviceName)
    {
        return string.Join("", serviceName.Split('-')
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Private Helper Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses x-event-subscriptions from event schema lines and adds to the dictionary.
    /// </summary>
    private static void ParseEventSubscriptions(
        string[] lines, string serviceName, Dictionary<string, List<string>> subscriptions)
    {
        var inSubscriptions = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("x-event-subscriptions:", StringComparison.Ordinal))
            {
                // Check for empty: "x-event-subscriptions: []"
                if (trimmed.Contains("[]", StringComparison.Ordinal))
                    continue;
                inSubscriptions = true;
                continue;
            }

            // Exit subscriptions block at the next top-level key
            if (inSubscriptions && trimmed.Length > 0 && !char.IsWhiteSpace(lines[i][0])
                && !trimmed.StartsWith("-", StringComparison.Ordinal)
                && !trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                inSubscriptions = false;
            }

            // Exit at sibling x-* extension attributes at the same indentation level
            // (prevents bleeding from x-event-subscriptions into x-event-publications)
            if (inSubscriptions && trimmed.StartsWith("x-", StringComparison.Ordinal)
                && trimmed.Contains(':', StringComparison.Ordinal))
            {
                inSubscriptions = false;
            }

            if (inSubscriptions && trimmed.StartsWith("- topic:", StringComparison.Ordinal))
            {
                var topic = trimmed["- topic:".Length..].Trim();
                if (topic.StartsWith('"') && topic.EndsWith('"'))
                    topic = topic[1..^1];
                if (topic.StartsWith('\'') && topic.EndsWith('\''))
                    topic = topic[1..^1];

                if (!subscriptions.TryGetValue(topic, out var list))
                {
                    list = new List<string>();
                    subscriptions[topic] = list;
                }
                list.Add(serviceName);
            }
        }
    }
}
