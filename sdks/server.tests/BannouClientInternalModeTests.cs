using BeyondImmersion.Bannou.Client;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.Bannou.Server.Tests;

public class BannouClientInternalModeTests
{
    private static void SetPrivateField<T>(BannouClient client, string fieldName, T? value)
    {
        var field = typeof(BannouClient).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(client, value);
    }

    private static string InvokeBuildAuthorizationHeader(BannouClient client, bool allowAnonymousInternal)
    {
        var method = typeof(BannouClient).GetMethod("BuildAuthorizationHeader", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = method?.Invoke(client, new object[] { allowAnonymousInternal }) as string;
        return result ?? string.Empty;
    }

    [Fact]
    public async Task BuildAuthorizationHeader_WithAccessToken_UsesBearer()
    {
        await using var client = new BannouClient();
        SetPrivateField(client, "_accessToken", "jwt-token");

        var header = InvokeBuildAuthorizationHeader(client, allowAnonymousInternal: false);

        Assert.Equal("Bearer jwt-token", header);
    }

    [Fact]
    public async Task BuildAuthorizationHeader_ServiceToken_InternalHeader()
    {
        await using var client = new BannouClient();
        SetPrivateField(client, "_serviceToken", "secret");

        var header = InvokeBuildAuthorizationHeader(client, allowAnonymousInternal: false);

        Assert.Equal("Internal", header);
    }

    [Fact]
    public async Task BuildAuthorizationHeader_AnonymousInternal_InternalHeader()
    {
        await using var client = new BannouClient();

        var header = InvokeBuildAuthorizationHeader(client, allowAnonymousInternal: true);

        Assert.Equal("Internal", header);
    }

    [Fact]
    public async Task BuildAuthorizationHeader_NoTokensAndNotInternal_ReturnsEmpty()
    {
        await using var client = new BannouClient();

        var header = InvokeBuildAuthorizationHeader(client, allowAnonymousInternal: false);

        Assert.Equal(string.Empty, header);
    }
}
