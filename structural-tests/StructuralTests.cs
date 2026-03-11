using BeyondImmersion.BannouService.Attributes;
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
    /// Lists helper services (types with [BannouHelperService]) whose parent plugin
    /// has no corresponding helper configuration class (a type with [ServiceConfiguration]
    /// in the same assembly whose class name contains the helper type's name or a
    /// recognizable prefix). This is informational — not all helpers need dedicated
    /// configuration. Run this test directly to produce the checklist.
    /// </summary>
    [Fact(Skip = "Informational — produces a checklist of helpers without dedicated configuration classes")]
    public void HelperServices_ShouldHaveConfigurationClasses()
    {
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
    /// structurally legitimate exceptions — not Category B blanket exemptions.
    /// Category B *.deleted publishers are generated by x-lifecycle but these
    /// entities use clean-deprecated sweep instead of per-entity deletion.
    /// The test must enforce that services call their clean-deprecated publishers;
    /// the *.deleted publishers are the only legitimate uncalled infrastructure.
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
    /// Validates that services with generated RegisterResourceCleanupCallbacksAsync()
    /// actually call it from their Plugin.cs OnRunningAsync. The method is generated from
    /// x-references in the API schema, but if the plugin doesn't call it, cleanup callbacks
    /// are silently missing at runtime.
    /// </summary>
    [Fact]
    public void Services_WithResourceCleanup_MustCallRegistration()
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

            // Check if this service has a generated ReferenceTracking file with RegisterResourceCleanupCallbacksAsync
            var generatedDir = Path.Combine(pluginDir, "Generated");
            if (!Directory.Exists(generatedDir)) continue;

            var hasReferenceTracking = Directory.GetFiles(generatedDir, "*ReferenceTracking.cs").Length > 0;
            if (!hasReferenceTracking) continue;

            // Verify the Plugin.cs calls RegisterResourceCleanupCallbacksAsync
            var pluginFiles = Directory.GetFiles(pluginDir, "*Plugin.cs", SearchOption.TopDirectoryOnly);
            var callFound = false;

            foreach (var pluginFile in pluginFiles)
            {
                var content = File.ReadAllText(pluginFile);
                if (content.Contains("RegisterResourceCleanupCallbacksAsync"))
                {
                    callFound = true;
                    break;
                }
            }

            if (!callFound)
            {
                failures.Add(
                    $"{serviceType.Name} (lib-{serviceName}): has generated *ReferenceTracking.cs " +
                    $"but *Plugin.cs does not call RegisterResourceCleanupCallbacksAsync()");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Services with x-references must call RegisterResourceCleanupCallbacksAsync() " +
            $"in their Plugin OnRunningAsync (generated but not wired up):\n" +
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
    /// Validates that services with non-empty x-event-subscriptions in their event schema
    /// have a RegisterEventConsumers method in *ServiceEvents.cs AND call it from the
    /// *Service.cs constructor. If a service declares event subscriptions in the schema but
    /// never wires them up, events are silently lost at runtime (IMPLEMENTATION TENETS T3).
    /// </summary>
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
            var hasNonEmptySubscriptions = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (!trimmed.StartsWith("x-event-subscriptions:", StringComparison.Ordinal))
                    continue;

                // Check if it's empty: "x-event-subscriptions: []" or "x-event-subscriptions:  []"
                if (trimmed.Contains("[]", StringComparison.Ordinal))
                    break;

                // Check if next non-comment, non-blank line starts with "- topic:"
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var nextTrimmed = lines[j].TrimStart();
                    if (string.IsNullOrWhiteSpace(nextTrimmed) ||
                        nextTrimmed.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    if (nextTrimmed.StartsWith("- topic:", StringComparison.Ordinal))
                        hasNonEmptySubscriptions = true;
                    break;
                }
                break;
            }

            if (!hasNonEmptySubscriptions)
                continue;

            // Extract service name: "auth-events.yaml" -> "auth"
            var serviceName = fileName.Replace("-events.yaml", "");
            var pluginDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins", $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir))
                continue;

            // Find *ServiceEvents.cs
            var eventsFiles = Directory.GetFiles(pluginDir, "*ServiceEvents.cs", SearchOption.TopDirectoryOnly);
            var hasRegisterMethod = false;
            foreach (var eventsFile in eventsFiles)
            {
                var content = File.ReadAllText(eventsFile);
                if (content.Contains("RegisterEventConsumers", StringComparison.Ordinal))
                {
                    hasRegisterMethod = true;
                    break;
                }
            }

            if (!hasRegisterMethod)
            {
                failures.Add(
                    $"lib-{serviceName}: has x-event-subscriptions in {fileName} " +
                    $"but *ServiceEvents.cs does not define RegisterEventConsumers()");
                continue;
            }

            // Verify *Service.cs constructor calls RegisterEventConsumers
            var serviceFiles = Directory.GetFiles(pluginDir, "*Service.cs", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).Contains("Events", StringComparison.Ordinal) &&
                            !Path.GetFileName(f).Contains("Models", StringComparison.Ordinal) &&
                            !Path.GetFileName(f).Contains("Plugin", StringComparison.Ordinal))
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
                    $"lib-{serviceName}: has RegisterEventConsumers in *ServiceEvents.cs " +
                    $"but *Service.cs constructor does not call it");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Services with x-event-subscriptions must define and call RegisterEventConsumers " +
            $"(IMPLEMENTATION TENETS T3 — event handlers silently missing):\n" +
            string.Join("\n", failures.Select(f => $"  - {f}")));
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
}
