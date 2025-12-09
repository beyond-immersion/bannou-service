using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.BannouService.UnitTests;

public class AssemblyResolveTests
{
    [Fact]
    public void OnAssemblyResolve_ReusesAlreadyLoadedAssemblyInstance()
    {
        // Arrange
        var programAssembly = typeof(BeyondImmersion.BannouService.Program).Assembly;
        var assemblyName = programAssembly.GetName().Name!;
        var beforeCount = AppDomain.CurrentDomain.GetAssemblies()
            .Count(a => a.GetName().Name == assemblyName);

        // Act: invoke the resolver explicitly with the current assembly name
        var resolver = typeof(BeyondImmersion.BannouService.Program)
            .GetMethod("OnAssemblyResolve", BindingFlags.NonPublic | BindingFlags.Static)!;

        var resolved = resolver.Invoke(null, new object?[]
        {
            null,
            new ResolveEventArgs(programAssembly.FullName!)
        }) as Assembly;

        var afterCount = AppDomain.CurrentDomain.GetAssemblies()
            .Count(a => a.GetName().Name == assemblyName);

        // Assert
        Assert.NotNull(resolved);
        Assert.Same(programAssembly, resolved);
        Assert.Equal(beforeCount, afterCount); // no duplicate load
    }
}
