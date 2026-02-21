// =============================================================================
// Client Endpoint Metadata Tests
// Tests for runtime endpoint type discovery (Phase 3).
// =============================================================================

using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.GameSession;
using Xunit;

namespace BeyondImmersion.Bannou.Client.Tests.TypedApi;

/// <summary>
/// Tests for the ClientEndpointMetadata class.
/// Verifies endpoint type lookup, registration, and service filtering.
/// </summary>
public class ClientEndpointMetadataTests
{
    // =========================================================================
    // GetRequestType TESTS
    // =========================================================================

    [Fact]
    public void GetRequestType_AuthLogin_ReturnsLoginRequest()
    {
        var requestType = ClientEndpointMetadata.GetRequestType("POST", "/auth/login");

        Assert.NotNull(requestType);
        Assert.Equal(typeof(LoginRequest), requestType);
    }

    [Fact]
    public void GetRequestType_CharacterGet_ReturnsGetCharacterRequest()
    {
        var requestType = ClientEndpointMetadata.GetRequestType("POST", "/character/get");

        Assert.NotNull(requestType);
        Assert.Equal(typeof(GetCharacterRequest), requestType);
    }

    [Fact]
    public void GetRequestType_UnknownEndpoint_ReturnsNull()
    {
        var requestType = ClientEndpointMetadata.GetRequestType("POST", "/not/real/endpoint");

        Assert.Null(requestType);
    }

    [Fact]
    public void GetRequestType_CaseInsensitiveMethod()
    {
        var requestType1 = ClientEndpointMetadata.GetRequestType("POST", "/auth/login");
        var requestType2 = ClientEndpointMetadata.GetRequestType("post", "/auth/login");
        var requestType3 = ClientEndpointMetadata.GetRequestType("Post", "/auth/login");

        Assert.Equal(requestType1, requestType2);
        Assert.Equal(requestType2, requestType3);
    }

    // =========================================================================
    // GetResponseType TESTS
    // =========================================================================

    [Fact]
    public void GetResponseType_AuthLogin_ReturnsAuthResponse()
    {
        var responseType = ClientEndpointMetadata.GetResponseType("POST", "/auth/login");

        Assert.NotNull(responseType);
        Assert.Equal(typeof(LoginResponse), responseType);
    }

    [Fact]
    public void GetResponseType_CharacterGet_ReturnsCharacterResponse()
    {
        var responseType = ClientEndpointMetadata.GetResponseType("POST", "/character/get");

        Assert.NotNull(responseType);
        Assert.Equal(typeof(CharacterResponse), responseType);
    }

    [Fact]
    public void GetResponseType_UnknownEndpoint_ReturnsNull()
    {
        var responseType = ClientEndpointMetadata.GetResponseType("POST", "/not/real/endpoint");

        Assert.Null(responseType);
    }

    // =========================================================================
    // GetEndpointInfo TESTS
    // =========================================================================

    [Fact]
    public void GetEndpointInfo_ValidEndpoint_ReturnsInfo()
    {
        var info = ClientEndpointMetadata.GetEndpointInfo("POST", "/auth/login");

        Assert.NotNull(info);
        Assert.Equal("POST", info.Method);
        Assert.Equal("/auth/login", info.Path);
        Assert.Equal("Auth", info.Service);
        Assert.NotNull(info.RequestType);
        Assert.NotNull(info.ResponseType);
    }

    [Fact]
    public void GetEndpointInfo_UnknownEndpoint_ReturnsNull()
    {
        var info = ClientEndpointMetadata.GetEndpointInfo("POST", "/not/real");

        Assert.Null(info);
    }

    [Fact]
    public void GetEndpointInfo_Summary_NotEmpty()
    {
        var info = ClientEndpointMetadata.GetEndpointInfo("POST", "/auth/login");

        Assert.NotNull(info);
        Assert.False(string.IsNullOrEmpty(info.Summary));
    }

    // =========================================================================
    // IsRegistered TESTS
    // =========================================================================

    [Fact]
    public void IsRegistered_KnownEndpoint_ReturnsTrue()
    {
        var isRegistered = ClientEndpointMetadata.IsRegistered("POST", "/auth/login");

        Assert.True(isRegistered);
    }

    [Fact]
    public void IsRegistered_UnknownEndpoint_ReturnsFalse()
    {
        var isRegistered = ClientEndpointMetadata.IsRegistered("POST", "/not/real");

        Assert.False(isRegistered);
    }

    [Fact]
    public void IsRegistered_WrongMethod_ReturnsFalse()
    {
        // Auth login is POST, not GET
        var isRegistered = ClientEndpointMetadata.IsRegistered("GET", "/auth/login");

        Assert.False(isRegistered);
    }

    // =========================================================================
    // GetAllEndpoints TESTS
    // =========================================================================

    [Fact]
    public void GetAllEndpoints_ReturnsNonEmptyCollection()
    {
        var endpoints = ClientEndpointMetadata.GetAllEndpoints().ToList();

        Assert.NotEmpty(endpoints);
    }

    [Fact]
    public void GetAllEndpoints_ContainsAuthEndpoints()
    {
        var endpoints = ClientEndpointMetadata.GetAllEndpoints().ToList();

        var loginEndpoint = endpoints.FirstOrDefault(e => e.Path == "/auth/login");
        Assert.NotNull(loginEndpoint);
        Assert.Equal("POST", loginEndpoint.Method);
    }

    [Fact]
    public void Count_MatchesGetAllEndpoints()
    {
        var count = ClientEndpointMetadata.Count;
        var endpoints = ClientEndpointMetadata.GetAllEndpoints().ToList();

        Assert.Equal(count, endpoints.Count);
    }

    [Fact]
    public void Count_IsReasonableNumber()
    {
        // We expect hundreds of endpoints across all services
        var count = ClientEndpointMetadata.Count;

        Assert.True(count > 800, $"Expected > 800 endpoints, got {count}");
        Assert.True(count < 900, $"Expected < 900 endpoints, got {count}");
    }

    // =========================================================================
    // GetEndpointsByService TESTS
    // =========================================================================

    [Fact]
    public void GetEndpointsByService_Auth_ReturnsAuthEndpoints()
    {
        var authEndpoints = ClientEndpointMetadata.GetEndpointsByService("Auth").ToList();

        Assert.NotEmpty(authEndpoints);
        Assert.All(authEndpoints, ep => Assert.Equal("Auth", ep.Service));
    }

    [Fact]
    public void GetEndpointsByService_Character_ReturnsCharacterEndpoints()
    {
        var characterEndpoints = ClientEndpointMetadata.GetEndpointsByService("Character").ToList();

        Assert.NotEmpty(characterEndpoints);
        Assert.All(characterEndpoints, ep => Assert.Equal("Character", ep.Service));
    }

    [Fact]
    public void GetEndpointsByService_CaseInsensitive()
    {
        var endpoints1 = ClientEndpointMetadata.GetEndpointsByService("auth").ToList();
        var endpoints2 = ClientEndpointMetadata.GetEndpointsByService("AUTH").ToList();
        var endpoints3 = ClientEndpointMetadata.GetEndpointsByService("Auth").ToList();

        Assert.Equal(endpoints1.Count, endpoints2.Count);
        Assert.Equal(endpoints2.Count, endpoints3.Count);
    }

    [Fact]
    public void GetEndpointsByService_UnknownService_ReturnsEmpty()
    {
        var endpoints = ClientEndpointMetadata.GetEndpointsByService("NotARealService").ToList();

        Assert.Empty(endpoints);
    }

    // =========================================================================
    // TYPE CONSISTENCY TESTS
    // =========================================================================

    [Fact]
    public void AllEndpoints_HaveValidRequestOrResponseType()
    {
        var endpoints = ClientEndpointMetadata.GetAllEndpoints();

        foreach (var endpoint in endpoints)
        {
            // Each endpoint should have at least a request or response type
            Assert.True(
                endpoint.RequestType != null || endpoint.ResponseType != null,
                $"Endpoint {endpoint.Method} {endpoint.Path} has neither request nor response type");
        }
    }

    [Fact]
    public void AllEndpoints_HaveValidService()
    {
        var endpoints = ClientEndpointMetadata.GetAllEndpoints();

        foreach (var endpoint in endpoints)
        {
            Assert.False(
                string.IsNullOrEmpty(endpoint.Service),
                $"Endpoint {endpoint.Method} {endpoint.Path} has empty service");
        }
    }

    [Fact]
    public void AllEndpoints_HaveValidMethod()
    {
        var validMethods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH" };
        var endpoints = ClientEndpointMetadata.GetAllEndpoints();

        foreach (var endpoint in endpoints)
        {
            Assert.Contains(endpoint.Method, validMethods);
        }
    }

    [Fact]
    public void AllEndpoints_PathStartsWithSlash()
    {
        var endpoints = ClientEndpointMetadata.GetAllEndpoints();

        foreach (var endpoint in endpoints)
        {
            Assert.StartsWith("/", endpoint.Path);
        }
    }
}
