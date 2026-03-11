using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.BannouService.TestUtilities;

// ⛔ FROZEN FILE — DO NOT MODIFY WITHOUT EXPLICIT USER PERMISSION ⛔
// This validator enforces key builder patterns across ALL services and helper services.
// Changes to match heuristics (GetPrefixConstants, GetBuildKeyMethods, GetGetKeyMethods)
// affect validation of every class with [BannouService] or [BannouHelperService] attributes.
// If a test failure appears to be a false positive, present evidence to the user and WAIT.

/// <summary>
/// Validates state store key builder patterns per FOUNDATION TENETS:
/// - Key prefixes must be private const string fields
/// - Keys must be constructed via internal static Build*Key() methods
/// - Builder visibility must be internal static (not private static)
/// - Naming must use Build prefix (not Get)
/// Also validates that all non-indirect state stores are referenced by their owning service.
/// </summary>
public static class StateStoreKeyValidator
{
    /// <summary>
    /// Validates that a service type follows the state store key builder pattern.
    /// Checks for prefix constants, builder methods, correct visibility, and naming.
    /// </summary>
    /// <typeparam name="TService">The service type to validate.</typeparam>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown when validation fails.</exception>
    public static void ValidateKeyBuilders<TService>() where TService : class
    {
        ValidateKeyBuilders(typeof(TService));
    }

    /// <summary>
    /// Validates that a service type follows the state store key builder pattern.
    /// </summary>
    /// <param name="serviceType">The service type to validate.</param>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown when validation fails.</exception>
    public static void ValidateKeyBuilders(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        var prefixFields = GetPrefixConstants(serviceType);
        var buildMethods = GetBuildKeyMethods(serviceType);
        var getMethods = GetGetKeyMethods(serviceType);

        // If no prefix constants and no builder methods, the service doesn't use key builders.
        // This is only valid if the service has no state stores — caller decides whether to invoke.
        if (prefixFields.Length == 0 && buildMethods.Length == 0 && getMethods.Length == 0)
        {
            return;
        }

        // If prefix constants exist, builder methods must also exist
        Assert.True(
            prefixFields.Length == 0 || buildMethods.Length > 0,
            $"{serviceType.Name} has {prefixFields.Length} prefix constant(s) " +
            $"({string.Join(", ", prefixFields.Select(f => f.Name))}) " +
            "but no Build*Key() or Build*() methods. " +
            "Add internal static string Build*Key() methods per FOUNDATION TENETS.");

        // If builder methods exist, prefix constants must also exist
        Assert.True(
            buildMethods.Length == 0 || prefixFields.Length > 0,
            $"{serviceType.Name} has {buildMethods.Length} Build*() method(s) " +
            $"({string.Join(", ", buildMethods.Select(m => m.Name))}) " +
            "but no *PREFIX* or *KEY_PREFIX constants. " +
            "Add private const string prefix fields per FOUNDATION TENETS.");

        // FORBIDDEN: Get*Key naming for key construction (implies store retrieval)
        Assert.True(
            getMethods.Length == 0,
            $"{serviceType.Name} uses Get*Key naming for key construction: " +
            $"{string.Join(", ", getMethods.Select(m => m.Name))}. " +
            "Rename to Build*Key — 'Get' implies store retrieval per FOUNDATION TENETS.");

        // Builder methods must be internal static (not private static)
        foreach (var method in buildMethods)
        {
            Assert.True(
                method.IsAssembly || method.IsFamilyOrAssembly,
                $"{serviceType.Name}.{method.Name}() is {GetVisibility(method)} but must be " +
                "internal static. Provider factories and tests need access per FOUNDATION TENETS.");
        }
    }

    private static FieldInfo[] GetPrefixConstants(Type type)
    {
        return type
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral
                        && !f.IsInitOnly
                        && f.FieldType == typeof(string)
                        && (f.Name.Contains("PREFIX") || f.Name.Contains("KEY_PREFIX") || f.Name.Contains("SUFFIX")))
            .ToArray();
    }

    private static MethodInfo[] GetBuildKeyMethods(Type type)
    {
        return type
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.IsStatic
                        && m.ReturnType == typeof(string)
                        && m.Name.StartsWith("Build", StringComparison.Ordinal)
                        && m.Name.Contains("Key", StringComparison.Ordinal))
            .ToArray();
    }

    private static MethodInfo[] GetGetKeyMethods(Type type)
    {
        return type
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.IsStatic
                        && m.ReturnType == typeof(string)
                        && m.Name.StartsWith("Get", StringComparison.Ordinal)
                        && m.Name.EndsWith("Key", StringComparison.Ordinal))
            .ToArray();
    }

    private static string GetVisibility(MethodInfo method)
    {
        if (method.IsPublic) return "public";
        if (method.IsPrivate) return "private";
        if (method.IsAssembly) return "internal";
        if (method.IsFamily) return "protected";
        if (method.IsFamilyOrAssembly) return "protected internal";
        return "unknown";
    }

    /// <summary>
    /// Validates that a service type references all of its non-indirect state stores.
    /// Checks that if StateStoreDefinitions.Metadata declares stores for this service
    /// (with IndirectOnly=false), the service injects IStateStoreFactory in its constructor.
    /// </summary>
    /// <typeparam name="TService">The service type to validate.</typeparam>
    public static void ValidateStoreReferences<TService>() where TService : class
    {
        ValidateStoreReferences(typeof(TService));
    }

    /// <summary>
    /// Validates that a service type references all of its non-indirect state stores.
    /// </summary>
    /// <param name="serviceType">The service type to validate.</param>
    public static void ValidateStoreReferences(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        // Get the service name from [BannouService] attribute
        var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
        if (attr == null)
        {
            return; // Not a BannouService — nothing to validate
        }

        var servicePascal = KebabToPascalCase(attr.Name);

        // Find all non-indirect stores for this service
        var serviceStores = StateStoreDefinitions.Metadata
            .Where(kvp => kvp.Value.Service == servicePascal && !kvp.Value.IndirectOnly)
            .ToList();

        if (serviceStores.Count == 0)
        {
            return; // No non-indirect stores for this service
        }

        // Check that the service constructor accepts IStateStoreFactory
        var constructors = serviceType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var hasFactory = constructors.Any(ctor =>
            ctor.GetParameters().Any(p => p.ParameterType == typeof(IStateStoreFactory)));

        // Also check helper services in the same assembly — some services delegate
        // store access to helper services (e.g., AuthService delegates to TokenService).
        // We check the primary service type only; if it doesn't inject the factory,
        // the stores must be accessed through helper services registered in the same plugin.
        if (!hasFactory)
        {
            // Check if any type in the same assembly injects IStateStoreFactory
            var assemblyTypes = serviceType.Assembly.GetTypes();
            var anyTypeHasFactory = assemblyTypes.Any(t =>
                t != serviceType &&
                !t.IsAbstract &&
                !t.IsInterface &&
                t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .Any(ctor => ctor.GetParameters()
                        .Any(p => p.ParameterType == typeof(IStateStoreFactory))));

            Assert.True(
                anyTypeHasFactory,
                $"{serviceType.Name} has {serviceStores.Count} non-indirect state store(s) " +
                $"({string.Join(", ", serviceStores.Select(s => s.Key))}) " +
                "but neither it nor any type in its assembly injects IStateStoreFactory. " +
                "Either use the stores or mark them indirectOnly in schemas/state-stores.yaml.");
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
