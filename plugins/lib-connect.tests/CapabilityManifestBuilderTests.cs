using BeyondImmersion.BannouService.Connect.Helpers;
using BeyondImmersion.BannouService.Connect.Protocol;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for CapabilityManifestBuilder.
/// Tests the pure logic for building capability manifests from service mappings and shortcuts.
/// </summary>
public class CapabilityManifestBuilderTests
{
    private readonly Mock<ILogger<CapabilityManifestBuilder>> _mockLogger;
    private readonly CapabilityManifestBuilder _builder;

    public CapabilityManifestBuilderTests()
    {
        _mockLogger = new Mock<ILogger<CapabilityManifestBuilder>>();
        _builder = new CapabilityManifestBuilder(_mockLogger.Object);
    }

    #region ParseEndpointKey Tests

    [Fact]
    public void ParseEndpointKey_ValidFormat_ReturnsCorrectParts()
    {
        // Arrange
        var endpointKey = "account:POST:/account/get";

        // Act
        var result = _builder.ParseEndpointKey(endpointKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("account", result.ServiceName);
        Assert.Equal("POST", result.Method);
        Assert.Equal("/account/get", result.Path);
    }

    [Fact]
    public void ParseEndpointKey_GetEndpoint_ReturnsCorrectParts()
    {
        // Arrange
        var endpointKey = "website:GET:/website/status";

        // Act
        var result = _builder.ParseEndpointKey(endpointKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("website", result.ServiceName);
        Assert.Equal("GET", result.Method);
        Assert.Equal("/website/status", result.Path);
    }

    [Fact]
    public void ParseEndpointKey_WithTemplateParams_IdentifiesTemplateCorrectly()
    {
        // Arrange
        var endpointKey = "auth:POST:/auth/oauth/{provider}/callback";

        // Act
        var result = _builder.ParseEndpointKey(endpointKey);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.HasTemplateParams);
        Assert.Equal("/auth/oauth/{provider}/callback", result.Path);
    }

    [Fact]
    public void ParseEndpointKey_WithoutTemplateParams_IdentifiesCorrectly()
    {
        // Arrange
        var endpointKey = "account:POST:/account/create";

        // Act
        var result = _builder.ParseEndpointKey(endpointKey);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.HasTemplateParams);
    }

    [Fact]
    public void ParseEndpointKey_PostEndpoint_IdentifiesAsPost()
    {
        // Arrange
        var endpointKey = "auth:POST:/auth/login";

        // Act
        var result = _builder.ParseEndpointKey(endpointKey);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsPost);
    }

    [Fact]
    public void ParseEndpointKey_GetEndpoint_IdentifiesAsNotPost()
    {
        // Arrange
        var endpointKey = "website:GET:/website/status";

        // Act
        var result = _builder.ParseEndpointKey(endpointKey);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsPost);
    }

    [Fact]
    public void ParseEndpointKey_NoColons_ReturnsNull()
    {
        // Arrange
        var endpointKey = "invalid-endpoint-key";

        // Act
        var result = _builder.ParseEndpointKey(endpointKey);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseEndpointKey_EmptyServiceName_ReturnsNull()
    {
        // Arrange
        var endpointKey = ":POST:/path";

        // Act
        var result = _builder.ParseEndpointKey(endpointKey);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseEndpointKey_MethodOnly_ReturnsWithEmptyPath()
    {
        // Arrange - Edge case: only service and method, no path
        var endpointKey = "service:POST";

        // Act
        var result = _builder.ParseEndpointKey(endpointKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("service", result.ServiceName);
        Assert.Equal("POST", result.Method);
        Assert.Equal("", result.Path);
    }

    [Fact]
    public void ParseEndpointKey_NestedPath_ParsesCorrectly()
    {
        // Arrange
        var endpointKey = "character:POST:/character/personality/get";

        // Act
        var result = _builder.ParseEndpointKey(endpointKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("character", result.ServiceName);
        Assert.Equal("POST", result.Method);
        Assert.Equal("/character/personality/get", result.Path);
    }

    #endregion

    #region BuildApiList Tests

    [Fact]
    public void BuildApiList_ValidPostEndpoints_ReturnsAllEndpoints()
    {
        // Arrange
        var mappings = new Dictionary<string, Guid>
        {
            ["account:POST:/account/get"] = Guid.NewGuid(),
            ["account:POST:/account/create"] = Guid.NewGuid(),
            ["auth:POST:/auth/login"] = Guid.NewGuid()
        };

        // Act
        var result = _builder.BuildApiList(mappings);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void BuildApiList_FiltersByService_ReturnsOnlyMatchingService()
    {
        // Arrange
        var mappings = new Dictionary<string, Guid>
        {
            ["account:POST:/account/get"] = Guid.NewGuid(),
            ["account:POST:/account/create"] = Guid.NewGuid(),
            ["auth:POST:/auth/login"] = Guid.NewGuid()
        };

        // Act
        var result = _builder.BuildApiList(mappings, serviceFilter: "account");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, api => Assert.Equal("account", api.ServiceName));
    }

    [Fact]
    public void BuildApiList_ServiceFilterCaseInsensitive_ReturnsMatchingService()
    {
        // Arrange
        var mappings = new Dictionary<string, Guid>
        {
            ["Account:POST:/account/get"] = Guid.NewGuid(),
            ["ACCOUNT:POST:/account/create"] = Guid.NewGuid()
        };

        // Act
        var result = _builder.BuildApiList(mappings, serviceFilter: "account");

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void BuildApiList_SkipsTemplateEndpoints_ReturnsOnlyNonTemplate()
    {
        // Arrange
        var mappings = new Dictionary<string, Guid>
        {
            ["auth:POST:/auth/login"] = Guid.NewGuid(),
            ["auth:POST:/auth/oauth/{provider}/callback"] = Guid.NewGuid(),
            ["website:GET:/website/content/{slug}"] = Guid.NewGuid()
        };

        // Act
        var result = _builder.BuildApiList(mappings);

        // Assert
        Assert.Single(result);
        Assert.Equal("/auth/login", result[0].Path);
    }

    [Fact]
    public void BuildApiList_SkipsNonPostEndpoints_ReturnsOnlyPost()
    {
        // Arrange
        var mappings = new Dictionary<string, Guid>
        {
            ["auth:POST:/auth/login"] = Guid.NewGuid(),
            ["website:GET:/website/status"] = Guid.NewGuid(),
            ["auth:GET:/auth/oauth/google/init"] = Guid.NewGuid()
        };

        // Act
        var result = _builder.BuildApiList(mappings);

        // Assert
        Assert.Single(result);
        Assert.Equal("POST", result[0].Method);
    }

    [Fact]
    public void BuildApiList_SkipsInvalidFormatEndpoints_ReturnsOnlyValid()
    {
        // Arrange
        var mappings = new Dictionary<string, Guid>
        {
            ["account:POST:/account/get"] = Guid.NewGuid(),
            ["invalid-endpoint-key"] = Guid.NewGuid(),
            [":POST:/orphaned-path"] = Guid.NewGuid()
        };

        // Act
        var result = _builder.BuildApiList(mappings);

        // Assert
        Assert.Single(result);
        Assert.Equal("/account/get", result[0].Path);
    }

    [Fact]
    public void BuildApiList_EmptyMappings_ReturnsEmptyList()
    {
        // Arrange
        var mappings = new Dictionary<string, Guid>();

        // Act
        var result = _builder.BuildApiList(mappings);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void BuildApiList_SetsCorrectEndpointKey_FormatsAsMethodPath()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var mappings = new Dictionary<string, Guid>
        {
            ["account:POST:/account/get"] = guid
        };

        // Act
        var result = _builder.BuildApiList(mappings);

        // Assert
        Assert.Single(result);
        Assert.Equal("POST:/account/get", result[0].EndpointKey);
    }

    [Fact]
    public void BuildApiList_PreservesServiceGuid_MatchesInputGuid()
    {
        // Arrange
        var expectedGuid = Guid.NewGuid();
        var mappings = new Dictionary<string, Guid>
        {
            ["account:POST:/account/get"] = expectedGuid
        };

        // Act
        var result = _builder.BuildApiList(mappings);

        // Assert
        Assert.Single(result);
        Assert.Equal(expectedGuid, result[0].ServiceGuid);
    }

    [Fact]
    public void BuildApiList_ServiceFilterWithPartialMatch_ReturnsMatchingPrefix()
    {
        // Arrange
        var mappings = new Dictionary<string, Guid>
        {
            ["character:POST:/character/get"] = Guid.NewGuid(),
            ["character-history:POST:/character-history/get-participation"] = Guid.NewGuid(),
            ["character-personality:POST:/character-personality/get"] = Guid.NewGuid(),
            ["auth:POST:/auth/login"] = Guid.NewGuid()
        };

        // Act
        var result = _builder.BuildApiList(mappings, serviceFilter: "character");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.All(result, api => Assert.StartsWith("character", api.ServiceName));
    }

    [Fact]
    public void BuildApiList_NoMatchingServiceFilter_ReturnsEmptyList()
    {
        // Arrange
        var mappings = new Dictionary<string, Guid>
        {
            ["account:POST:/account/get"] = Guid.NewGuid(),
            ["auth:POST:/auth/login"] = Guid.NewGuid()
        };

        // Act
        var result = _builder.BuildApiList(mappings, serviceFilter: "nonexistent");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void BuildApiList_NullServiceFilter_ReturnsAllEndpoints()
    {
        // Arrange
        var mappings = new Dictionary<string, Guid>
        {
            ["account:POST:/account/get"] = Guid.NewGuid(),
            ["auth:POST:/auth/login"] = Guid.NewGuid()
        };

        // Act
        var result = _builder.BuildApiList(mappings, serviceFilter: null);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void BuildApiList_EmptyServiceFilter_ReturnsAllEndpoints()
    {
        // Arrange
        var mappings = new Dictionary<string, Guid>
        {
            ["account:POST:/account/get"] = Guid.NewGuid(),
            ["auth:POST:/auth/login"] = Guid.NewGuid()
        };

        // Act
        var result = _builder.BuildApiList(mappings, serviceFilter: "");

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region BuildShortcutList Tests

    [Fact]
    public void BuildShortcutList_ValidShortcuts_ReturnsAllShortcuts()
    {
        // Arrange
        var shortcuts = new List<SessionShortcutData>
        {
            CreateValidShortcut(),
            CreateValidShortcut(),
            CreateValidShortcut()
        };

        // Act
        var result = _builder.BuildShortcutList(shortcuts);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void BuildShortcutList_ExpiredShortcut_SkipsAndNotifiesCallback()
    {
        // Arrange
        var expiredGuid = Guid.NewGuid();
        var validGuid = Guid.NewGuid();
        var shortcuts = new List<SessionShortcutData>
        {
            CreateValidShortcut(validGuid),
            CreateExpiredShortcut(expiredGuid)
        };

        var callbackInvoked = false;
        Guid? callbackGuid = null;

        // Act
        var result = _builder.BuildShortcutList(shortcuts, (guid) =>
        {
            callbackInvoked = true;
            callbackGuid = guid;
        });

        // Assert
        Assert.Single(result);
        Assert.Equal(validGuid, result[0].RouteGuid);
        Assert.True(callbackInvoked);
        Assert.Equal(expiredGuid, callbackGuid);
    }

    [Fact]
    public void BuildShortcutList_NullTargetService_SkipsAndNotifiesCallback()
    {
        // Arrange
        var invalidGuid = Guid.NewGuid();
        var shortcuts = new List<SessionShortcutData>
        {
            new SessionShortcutData
            {
                RouteGuid = invalidGuid,
                TargetGuid = Guid.NewGuid(),
                TargetService = null,
                TargetEndpoint = "/sessions/join",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        Guid? callbackGuid = null;

        // Act
        var result = _builder.BuildShortcutList(shortcuts, (guid) => callbackGuid = guid);

        // Assert
        Assert.Empty(result);
        Assert.Equal(invalidGuid, callbackGuid);
    }

    [Fact]
    public void BuildShortcutList_EmptyTargetEndpoint_SkipsAndNotifiesCallback()
    {
        // Arrange
        var invalidGuid = Guid.NewGuid();
        var shortcuts = new List<SessionShortcutData>
        {
            new SessionShortcutData
            {
                RouteGuid = invalidGuid,
                TargetGuid = Guid.NewGuid(),
                TargetService = "game-session",
                TargetEndpoint = "",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        Guid? callbackGuid = null;

        // Act
        var result = _builder.BuildShortcutList(shortcuts, (guid) => callbackGuid = guid);

        // Assert
        Assert.Empty(result);
        Assert.Equal(invalidGuid, callbackGuid);
    }

    [Fact]
    public void BuildShortcutList_NullCallback_DoesNotThrow()
    {
        // Arrange
        var shortcuts = new List<SessionShortcutData>
        {
            CreateExpiredShortcut()
        };

        // Act & Assert - Should not throw when callback is null
        var result = _builder.BuildShortcutList(shortcuts, null);
        Assert.Empty(result);
    }

    [Fact]
    public void BuildShortcutList_EmptyList_ReturnsEmptyResult()
    {
        // Arrange
        var shortcuts = new List<SessionShortcutData>();

        // Act
        var result = _builder.BuildShortcutList(shortcuts);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void BuildShortcutList_UsesNameOrFallsBackToGuid()
    {
        // Arrange
        var guidWithName = Guid.NewGuid();
        var guidWithoutName = Guid.NewGuid();
        var shortcuts = new List<SessionShortcutData>
        {
            CreateValidShortcut(guidWithName, name: "join-game"),
            CreateValidShortcut(guidWithoutName, name: null)
        };

        // Act
        var result = _builder.BuildShortcutList(shortcuts);

        // Assert
        Assert.Equal(2, result.Count);

        var withName = result.FirstOrDefault(s => s.RouteGuid == guidWithName);
        var withoutName = result.FirstOrDefault(s => s.RouteGuid == guidWithoutName);

        Assert.NotNull(withName);
        Assert.NotNull(withoutName);
        Assert.Equal("join-game", withName.Name);
        Assert.Equal(guidWithoutName.ToString(), withoutName.Name);
    }

    [Fact]
    public void BuildShortcutList_PreservesDescription()
    {
        // Arrange
        var shortcuts = new List<SessionShortcutData>
        {
            CreateValidShortcut(description: "Join the active game session")
        };

        // Act
        var result = _builder.BuildShortcutList(shortcuts);

        // Assert
        Assert.Single(result);
        Assert.Equal("Join the active game session", result[0].Description);
    }

    [Fact]
    public void BuildShortcutList_PreservesTargetServiceAndEndpoint()
    {
        // Arrange
        var shortcuts = new List<SessionShortcutData>
        {
            CreateValidShortcut(targetService: "game-session", targetEndpoint: "/sessions/join")
        };

        // Act
        var result = _builder.BuildShortcutList(shortcuts);

        // Assert
        Assert.Single(result);
        Assert.Equal("game-session", result[0].TargetService);
        Assert.Equal("/sessions/join", result[0].TargetEndpoint);
    }

    [Fact]
    public void BuildShortcutList_MultipleExpired_NotifiesForEach()
    {
        // Arrange
        var expiredGuids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var shortcuts = expiredGuids.Select(guid => CreateExpiredShortcut(guid)).ToList();

        var notifiedGuids = new List<Guid>();

        // Act
        var result = _builder.BuildShortcutList(shortcuts, (guid) => notifiedGuids.Add(guid));

        // Assert
        Assert.Empty(result);
        Assert.Equal(3, notifiedGuids.Count);
        Assert.All(expiredGuids, expected => Assert.Contains(expected, notifiedGuids));
    }

    [Fact]
    public void BuildShortcutList_MixedValidAndInvalid_FiltersCorrectly()
    {
        // Arrange
        var validGuid1 = Guid.NewGuid();
        var validGuid2 = Guid.NewGuid();
        var expiredGuid = Guid.NewGuid();
        var invalidGuid = Guid.NewGuid();

        var shortcuts = new List<SessionShortcutData>
        {
            CreateValidShortcut(validGuid1),
            CreateExpiredShortcut(expiredGuid),
            CreateValidShortcut(validGuid2),
            new SessionShortcutData
            {
                RouteGuid = invalidGuid,
                TargetGuid = Guid.NewGuid(),
                TargetService = null, // Invalid - missing required field
                TargetEndpoint = "/path",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var notifiedGuids = new List<Guid>();

        // Act
        var result = _builder.BuildShortcutList(shortcuts, (guid) => notifiedGuids.Add(guid));

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.RouteGuid == validGuid1);
        Assert.Contains(result, s => s.RouteGuid == validGuid2);
        Assert.Equal(2, notifiedGuids.Count);
        Assert.Contains(expiredGuid, notifiedGuids);
        Assert.Contains(invalidGuid, notifiedGuids);
    }

    #endregion

    #region Helper Methods

    private static SessionShortcutData CreateValidShortcut(
        Guid? routeGuid = null,
        string? name = "test-shortcut",
        string? description = null,
        string? targetService = "game-session",
        string? targetEndpoint = "/sessions/join")
    {
        return new SessionShortcutData
        {
            RouteGuid = routeGuid ?? Guid.NewGuid(),
            TargetGuid = Guid.NewGuid(),
            TargetService = targetService,
            TargetEndpoint = targetEndpoint,
            Name = name,
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) // Valid for 1 hour
        };
    }

    private static SessionShortcutData CreateExpiredShortcut(Guid? routeGuid = null)
    {
        return new SessionShortcutData
        {
            RouteGuid = routeGuid ?? Guid.NewGuid(),
            TargetGuid = Guid.NewGuid(),
            TargetService = "game-session",
            TargetEndpoint = "/sessions/join",
            Name = "expired-shortcut",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) // Expired 1 hour ago
        };
    }

    #endregion
}
