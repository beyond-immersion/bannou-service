using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Analytics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace BeyondImmersion.BannouService.Analytics.Tests;

/// <summary>
/// Architecture tests verifying Analytics is a leaf-node observer:
/// only injects L0/L1/L2 clients and only calls read methods on them.
/// </summary>
public class AnalyticsArchitectureTests
{
    /// <summary>
    /// Read method prefixes per standardized naming conventions (Quality Tenets T16).
    /// Any async client method NOT starting with one of these is assumed to be a write/mutation.
    /// </summary>
    private static readonly string[] ReadPrefixes =
        ["Get", "List", "Query", "Exists", "Search", "Find", "Check", "Count"];

    /// <summary>
    /// Approved service client dependencies for Analytics with their actual layers.
    /// Analytics may only inject clients from these foundation-layer services.
    /// Adding a new client dependency requires adding it here — the test will fail
    /// if an unapproved client appears in the constructor, forcing explicit review.
    /// </summary>
    private static readonly Dictionary<string, ServiceLayer> ApprovedClientDependencies = new()
    {
        ["IGameServiceClient"] = ServiceLayer.GameFoundation,
        ["IGameSessionClient"] = ServiceLayer.GameFoundation,
        ["IRealmClient"] = ServiceLayer.GameFoundation,
        ["ICharacterClient"] = ServiceLayer.GameFoundation,
    };

    /// <summary>
    /// Maximum allowed layer for Analytics' service client dependencies.
    /// Analytics is designed as a leaf-node observer — it should only inject
    /// foundation-layer clients (L0/L1/L2) for read-only entity resolution.
    /// </summary>
    private const ServiceLayer MaxAllowedClientLayer = ServiceLayer.GameFoundation;

    /// <summary>
    /// Analytics (L4) should only depend on L0, L1, and L2 services.
    /// While the hierarchy allows L4→L3/L4, Analytics is designed as an event-only
    /// observer with read-only resolution calls to foundation services.
    /// </summary>
    [Fact]
    public void AnalyticsService_OnlyInjectsFoundationClients()
    {
        var serviceType = typeof(AnalyticsService);
        var ctor = serviceType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Single(ctor);

        var clientParams = ctor[0].GetParameters()
            .Where(p => IsServiceClientInterface(p.ParameterType))
            .ToList();

        Assert.NotEmpty(clientParams);

        var violations = new List<string>();
        foreach (var param in clientParams)
        {
            var clientName = param.ParameterType.Name;

            if (!ApprovedClientDependencies.TryGetValue(clientName, out var layer))
            {
                violations.Add(
                    $"{param.Name}: {clientName} is not in the approved dependency list " +
                    "— review required before adding new client dependencies to Analytics");
            }
            else if (layer > MaxAllowedClientLayer)
            {
                violations.Add(
                    $"{param.Name}: {clientName} is {layer} " +
                    $"— exceeds maximum allowed layer ({MaxAllowedClientLayer}) for Analytics");
            }
        }

        Assert.True(violations.Count == 0,
            $"Analytics injects non-foundation clients:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Analytics should only call read methods on its injected service clients.
    /// This enforces the "leaf node for write calls" design constraint:
    /// Analytics observes via events and resolves entities via read-only lookups.
    /// </summary>
    [Fact]
    public void AnalyticsService_OnlyCallsReadMethodsOnClients()
    {
        var serviceType = typeof(AnalyticsService);
        var assemblyPath = serviceType.Assembly.Location;

        var ctor = serviceType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Single(ctor);

        var clientTypes = ctor[0].GetParameters()
            .Where(p => IsServiceClientInterface(p.ParameterType))
            .Select(p => p.ParameterType)
            .ToList();

        Assert.NotEmpty(clientTypes);

        var violations = new List<string>();

        foreach (var clientType in clientTypes)
        {
            // Get all async methods on the client interface
            var allAsyncMethods = clientType.GetMethods()
                .Where(m => m.Name.EndsWith("Async"))
                .Select(m => m.Name)
                .Distinct()
                .ToArray();

            if (allAsyncMethods.Length == 0)
                continue;

            // Find which methods are actually referenced by the Analytics assembly
            var referenced = GetReferencedMethodsOnType(
                assemblyPath, clientType.Name, allAsyncMethods);

            foreach (var method in referenced)
            {
                if (!IsReadMethod(method))
                {
                    violations.Add($"{clientType.Name}.{method}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Analytics calls non-read methods on service clients (should be read-only):{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations.Select(v => $"  - {v}")));
    }

    /// <summary>
    /// Checks if a method name represents a read operation based on naming conventions.
    /// </summary>
    private static bool IsReadMethod(string methodName)
    {
        return ReadPrefixes.Any(prefix =>
            methodName.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks if a type is a service client interface (I*Client pattern).
    /// Excludes infrastructure interfaces like IMeshInvocationClient.
    /// </summary>
    private static bool IsServiceClientInterface(Type type)
    {
        if (!type.IsInterface)
            return false;

        var name = type.Name;
        return name.StartsWith('I') && name.EndsWith("Client")
            && name != "IMeshInvocationClient";
    }

    /// <summary>
    /// Scans an assembly's MemberRef metadata table for cross-assembly method references
    /// to the specified type. Returns the subset of methodNames that are actually referenced.
    /// </summary>
    /// <remarks>
    /// Uses System.Reflection.Metadata for zero-load PE inspection — the same approach
    /// as AssemblyMetadataScanner in structural-tests, scoped to this test's needs.
    /// Only scans the MemberRef table (cross-assembly references), which is sufficient
    /// because client interfaces live in a different assembly (bannou-service).
    /// </remarks>
    private static HashSet<string> GetReferencedMethodsOnType(
        string assemblyPath, string typeName, string[] methodNames)
    {
        var result = new HashSet<string>();

        if (!File.Exists(assemblyPath))
            return result;

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
            return result;

        var reader = peReader.GetMetadataReader();
        var methodNameSet = new HashSet<string>(methodNames);

        foreach (var memberRefHandle in reader.MemberReferences)
        {
            var memberRef = reader.GetMemberReference(memberRefHandle);
            if (memberRef.Parent.Kind != HandleKind.TypeReference)
                continue;

            var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
            if (reader.GetString(typeRef.Name) != typeName)
                continue;

            var memberName = reader.GetString(memberRef.Name);
            if (methodNameSet.Contains(memberName))
                result.Add(memberName);
        }

        return result;
    }
}
