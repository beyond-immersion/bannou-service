using System.Reflection;
using Xunit;

namespace BeyondImmersion.Bannou.BehaviorExpressions.Tests;

/// <summary>
/// Placeholder smoke tests for the BehaviorExpressions SDK test project.
/// Verifies the referenced assembly loads and has the expected shape. Real unit
/// tests should replace this when the SDK's test coverage is authored.
/// </summary>
public class AssemblySmokeTest
{
    /// <summary>
    /// Verifies the BehaviorExpressions assembly loads and contains at least one type.
    /// </summary>
    [Fact]
    public void BehaviorExpressionsAssembly_Loads()
    {
        // The assembly is referenced by project reference; touching any type from its
        // public surface forces the loader to resolve it.
        var assembly = typeof(Exceptions.AbmlUndefinedException).Assembly;

        Assert.NotNull(assembly);
        Assert.StartsWith("BeyondImmersion.Bannou.BehaviorExpressions", assembly.GetName().Name);
        Assert.NotEmpty(assembly.GetTypes());
    }

    /// <summary>
    /// Verifies the expected top-level namespaces are present in the SDK assembly.
    /// </summary>
    [Fact]
    public void BehaviorExpressionsAssembly_ExposesExpectedNamespaces()
    {
        var assembly = typeof(Exceptions.AbmlUndefinedException).Assembly;

        var namespaces = assembly.GetTypes()
            .Select(t => t.Namespace)
            .Where(ns => ns != null)
            .Distinct()
            .ToList();

        Assert.Contains("BeyondImmersion.Bannou.BehaviorExpressions.Exceptions", namespaces);
        Assert.Contains("BeyondImmersion.Bannou.BehaviorExpressions.Expressions", namespaces);
        Assert.Contains("BeyondImmersion.Bannou.BehaviorExpressions.Runtime", namespaces);
    }
}
