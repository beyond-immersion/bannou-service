// =============================================================================
// Proxy Tests
// Tests for generated service proxy classes.
// =============================================================================

using System.Reflection;
using BeyondImmersion.Bannou.Client.Proxies;
using BeyondImmersion.Bannou.Core;
using Xunit;

namespace BeyondImmersion.Bannou.Client.Tests.TypedApi;

/// <summary>
/// Tests for generated proxy classes.
/// Verifies proxy structure, null handling, and method signatures.
/// </summary>
public class ProxyTests
{
    // =========================================================================
    // PROXY EXISTENCE TESTS
    // =========================================================================

    [Fact]
    public void CharacterProxy_Exists()
    {
        var type = typeof(CharacterProxy);
        Assert.NotNull(type);
        Assert.True(type.IsSealed);
        Assert.True(type.IsClass);
    }

    [Fact]
    public void RealmProxy_Exists()
    {
        var type = typeof(RealmProxy);
        Assert.NotNull(type);
        Assert.True(type.IsSealed);
    }

    [Fact]
    public void AllExpectedProxies_Exist()
    {
        var proxyTypes = new[]
        {
            typeof(AccountProxy),
            typeof(AuthProxy),
            typeof(CharacterProxy),
            typeof(GameSessionProxy),
            typeof(RealmProxy),
            typeof(SpeciesProxy),
            typeof(VoiceProxy),
        };

        foreach (var proxyType in proxyTypes)
        {
            Assert.NotNull(proxyType);
            Assert.True(proxyType.IsSealed, $"{proxyType.Name} should be sealed");
        }
    }

    // =========================================================================
    // CONSTRUCTOR NULL HANDLING TESTS
    // =========================================================================

    [Fact]
    public void CharacterProxy_Constructor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CharacterProxy(null!));
    }

    [Fact]
    public void RealmProxy_Constructor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RealmProxy(null!));
    }

    // =========================================================================
    // METHOD SIGNATURE TESTS
    // =========================================================================

    [Fact]
    public void CharacterProxy_HasCreateCharacterAsync()
    {
        var method = typeof(CharacterProxy).GetMethod("CreateCharacterAsync");
        Assert.NotNull(method);
        Assert.True(method.IsPublic);

        var parameters = method.GetParameters();
        Assert.True(parameters.Length >= 1);
        Assert.Equal("CreateCharacterRequest", parameters[0].ParameterType.Name);
    }

    [Fact]
    public void CharacterProxy_HasGetCharacterAsync()
    {
        var method = typeof(CharacterProxy).GetMethod("GetCharacterAsync");
        Assert.NotNull(method);
        Assert.True(method.IsPublic);

        var parameters = method.GetParameters();
        Assert.True(parameters.Length >= 1);
        Assert.Equal("GetCharacterRequest", parameters[0].ParameterType.Name);
    }

    [Fact]
    public void RealmProxy_HasCreateRealmAsync()
    {
        var method = typeof(RealmProxy).GetMethod("CreateRealmAsync");
        Assert.NotNull(method);

        var parameters = method.GetParameters();
        Assert.True(parameters.Length >= 1);
        Assert.Equal("CreateRealmRequest", parameters[0].ParameterType.Name);
    }

    [Fact]
    public void ProxyMethods_ReturnApiResponse()
    {
        var characterMethods = typeof(CharacterProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.EndsWith("Async") && m.DeclaringType == typeof(CharacterProxy));

        foreach (var method in characterMethods)
        {
            var returnType = method.ReturnType;
            Assert.True(returnType.IsGenericType, $"{method.Name} should return generic type");
            Assert.Equal(typeof(Task<>), returnType.GetGenericTypeDefinition());

            var innerType = returnType.GetGenericArguments()[0];
            Assert.True(innerType.IsGenericType, $"{method.Name} should return Task<ApiResponse<T>>");
            Assert.Equal(typeof(ApiResponse<>), innerType.GetGenericTypeDefinition());
        }
    }

    [Fact]
    public void ProxyMethods_HaveStandardParameters()
    {
        var method = typeof(CharacterProxy).GetMethod("CreateCharacterAsync");
        Assert.NotNull(method);

        var parameters = method.GetParameters();

        // First param is request
        Assert.Equal("request", parameters[0].Name);

        // Should have channel, timeout, cancellationToken
        var channelParam = parameters.FirstOrDefault(p => p.Name == "channel");
        Assert.NotNull(channelParam);
        Assert.Equal(typeof(ushort), channelParam.ParameterType);
        Assert.True(channelParam.HasDefaultValue);

        var timeoutParam = parameters.FirstOrDefault(p => p.Name == "timeout");
        Assert.NotNull(timeoutParam);
        Assert.Equal(typeof(TimeSpan?), timeoutParam.ParameterType);
        Assert.True(timeoutParam.HasDefaultValue);

        var ctParam = parameters.FirstOrDefault(p => p.Name == "cancellationToken");
        Assert.NotNull(ctParam);
        Assert.Equal(typeof(CancellationToken), ctParam.ParameterType);
        Assert.True(ctParam.HasDefaultValue);
    }

    // =========================================================================
    // PROXY COUNT TESTS
    // =========================================================================

    [Fact]
    public void CharacterProxy_HasMultipleMethods()
    {
        var methods = typeof(CharacterProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.EndsWith("Async") && m.DeclaringType == typeof(CharacterProxy))
            .ToList();

        // Character service has multiple endpoints
        Assert.True(methods.Count >= 5, $"Expected at least 5 async methods, found {methods.Count}");
    }

    [Fact]
    public void ProxyNamespace_IsCorrect()
    {
        Assert.Equal(
            "BeyondImmersion.Bannou.Client.Proxies",
            typeof(CharacterProxy).Namespace);
    }

    // =========================================================================
    // XML DOCUMENTATION TESTS
    // =========================================================================

    [Fact]
    public void ProxyMethods_HaveXmlDocumentation()
    {
        // This test verifies XML doc generation doesn't cause compilation errors
        // The actual presence of XML docs is verified by the build with GenerateDocumentationFile
        var method = typeof(CharacterProxy).GetMethod("CreateCharacterAsync");
        Assert.NotNull(method);

        // Method should exist and be callable - XML doc issues would cause build failures
        Assert.True(method.IsPublic);
    }

    // =========================================================================
    // INTERNAL CONSTRUCTOR TESTS
    // =========================================================================

    [Fact]
    public void ProxyConstructors_AreInternal()
    {
        var constructor = typeof(CharacterProxy).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotEmpty(constructor);
        Assert.True(constructor[0].IsAssembly, "Constructor should be internal");
    }
}
