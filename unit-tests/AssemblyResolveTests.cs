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
        var assemblyName = programAssembly.GetName().Name
            ?? throw new InvalidOperationException("Program assembly name is null");
        var beforeCount = AppDomain.CurrentDomain.GetAssemblies()
            .Count(a => a.GetName().Name == assemblyName);

        // Act: invoke the resolver explicitly with the current assembly name
        var resolver = typeof(BeyondImmersion.BannouService.Program)
            .GetMethod("OnAssemblyResolve", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("OnAssemblyResolve method not found");

        var fullName = programAssembly.FullName
            ?? throw new InvalidOperationException("Program assembly full name is null");
        var resolved = resolver.Invoke(null, new object?[]
        {
            null,
            new ResolveEventArgs(fullName)
        }) as Assembly;

        var afterCount = AppDomain.CurrentDomain.GetAssemblies()
            .Count(a => a.GetName().Name == assemblyName);

        // Assert
        Assert.NotNull(resolved);
        Assert.Same(programAssembly, resolved);
        Assert.Equal(beforeCount, afterCount); // no duplicate load
    }
}
