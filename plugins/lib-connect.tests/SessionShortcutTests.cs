using BeyondImmersion.BannouService.Connect.Protocol;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Comprehensive unit tests for Session Shortcuts functionality.
/// Tests GUID generation, ConnectionState management, and MessageRouter routing.
/// </summary>
public class SessionShortcutTests
{
    private readonly string _testServerSalt = "test-server-salt-2025";

    #region GuidGenerator - Session Shortcut GUID Tests

    /// <summary>
    /// Tests that GenerateSessionShortcutGuid produces a valid GUID with version 7 bits.
    /// </summary>
    [Fact]
    public void GenerateSessionShortcutGuid_ShouldProduceVersion7Guid()
    {
        // Arrange
        var sessionId = "test-session-123";
        var shortcutName = "join_game";
        var sourceService = "game-session";

        // Act
        var guid = GuidGenerator.GenerateSessionShortcutGuid(sessionId, shortcutName, sourceService, _testServerSalt);

        // Assert
        Assert.NotEqual(Guid.Empty, guid);
        Assert.True(GuidGenerator.IsSessionShortcutGuid(guid), "Generated GUID should have version 7 bits set");
    }

    /// <summary>
    /// Tests that IsSessionShortcutGuid correctly identifies v7 GUIDs vs v5 (service) and v6 (client-to-client).
    /// </summary>
    [Fact]
    public void IsSessionShortcutGuid_ShouldDistinguishFromServiceAndClientGuids()
    {
        // Arrange
        var sessionId = "test-session-123";
        var serviceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "account", _testServerSalt);
        var clientGuid = GuidGenerator.GenerateClientGuid(sessionId, "other-session", _testServerSalt);
        var shortcutGuid = GuidGenerator.GenerateSessionShortcutGuid(sessionId, "my_shortcut", "test-service", _testServerSalt);

        // Assert
        Assert.False(GuidGenerator.IsSessionShortcutGuid(serviceGuid), "Service GUID (v5) should not be identified as shortcut");
        Assert.False(GuidGenerator.IsSessionShortcutGuid(clientGuid), "Client GUID (v6) should not be identified as shortcut");
        Assert.True(GuidGenerator.IsSessionShortcutGuid(shortcutGuid), "Shortcut GUID (v7) should be identified as shortcut");

        // Cross-validation with other version checks
        Assert.True(GuidGenerator.IsServiceGuid(serviceGuid), "Service GUID should be identified as v5");
        Assert.True(GuidGenerator.IsClientGuid(clientGuid), "Client GUID should be identified as v6");
        Assert.False(GuidGenerator.IsServiceGuid(shortcutGuid), "Shortcut GUID should not be v5");
        Assert.False(GuidGenerator.IsClientGuid(shortcutGuid), "Shortcut GUID should not be v6");
    }

    /// <summary>
    /// Tests that session shortcut GUIDs are deterministic for the same inputs.
    /// </summary>
    [Fact]
    public void GenerateSessionShortcutGuid_ShouldBeDeterministic()
    {
        // Arrange
        var sessionId = "test-session";
        var shortcutName = "test_shortcut";
        var sourceService = "test-service";

        // Act
        var guid1 = GuidGenerator.GenerateSessionShortcutGuid(sessionId, shortcutName, sourceService, _testServerSalt);
        var guid2 = GuidGenerator.GenerateSessionShortcutGuid(sessionId, shortcutName, sourceService, _testServerSalt);

        // Assert
        Assert.Equal(guid1, guid2);
    }

    /// <summary>
    /// Tests that different inputs produce different shortcut GUIDs.
    /// </summary>
    [Fact]
    public void GenerateSessionShortcutGuid_ShouldBeDifferentForDifferentInputs()
    {
        // Arrange
        var sessionId = "test-session";

        // Act
        var guid1 = GuidGenerator.GenerateSessionShortcutGuid(sessionId, "shortcut1", "service1", _testServerSalt);
        var guid2 = GuidGenerator.GenerateSessionShortcutGuid(sessionId, "shortcut2", "service1", _testServerSalt);
        var guid3 = GuidGenerator.GenerateSessionShortcutGuid(sessionId, "shortcut1", "service2", _testServerSalt);
        var guid4 = GuidGenerator.GenerateSessionShortcutGuid("other-session", "shortcut1", "service1", _testServerSalt);

        // Assert - all should be different
        var guids = new[] { guid1, guid2, guid3, guid4 };
        Assert.Equal(4, guids.Distinct().Count());
    }

    /// <summary>
    /// Tests that GenerateSessionShortcutGuid throws for null/empty parameters.
    /// </summary>
    [Theory]
    [InlineData(null, "shortcut", "service", "salt")]
    [InlineData("session", null, "service", "salt")]
    [InlineData("session", "shortcut", null, "salt")]
    [InlineData("session", "shortcut", "service", null)]
    [InlineData("", "shortcut", "service", "salt")]
    [InlineData("session", "", "service", "salt")]
    [InlineData("session", "shortcut", "", "salt")]
    [InlineData("session", "shortcut", "service", "")]
    public void GenerateSessionShortcutGuid_ShouldThrowForInvalidInputs(
        string? sessionId, string? shortcutName, string? sourceService, string? serverSalt)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            GuidGenerator.GenerateSessionShortcutGuid(sessionId!, shortcutName!, sourceService!, serverSalt!));
    }

    #endregion

    #region ConnectionState - Shortcut Management Tests

    /// <summary>
    /// Tests adding and retrieving a shortcut from ConnectionState.
    /// </summary>
    [Fact]
    public void AddOrUpdateShortcut_ShouldStoreShortcut()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        var shortcut = CreateTestShortcut("join_game", "game-session");

        // Act
        connectionState.AddOrUpdateShortcut(shortcut);

        // Assert
        Assert.True(connectionState.TryGetShortcut(shortcut.RouteGuid, out var retrieved));
        Assert.NotNull(retrieved);
        Assert.Equal(shortcut.RouteGuid, retrieved.RouteGuid);
        Assert.Equal(shortcut.TargetGuid, retrieved.TargetGuid);
        Assert.Equal(shortcut.Name, retrieved.Name);
        Assert.Equal(shortcut.SourceService, retrieved.SourceService);
        Assert.Equal(1, connectionState.ShortcutCount);
    }

    /// <summary>
    /// Tests updating an existing shortcut.
    /// </summary>
    [Fact]
    public void AddOrUpdateShortcut_ShouldUpdateExistingShortcut()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        var originalShortcut = CreateTestShortcut("join_game", "game-session");
        connectionState.AddOrUpdateShortcut(originalShortcut);

        var updatedShortcut = new SessionShortcutData
        {
            RouteGuid = originalShortcut.RouteGuid,
            TargetGuid = Guid.NewGuid(), // Different target
            BoundPayload = new byte[] { 0xAB, 0xCD },
            SourceService = "game-session",
            Name = "join_game",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        connectionState.AddOrUpdateShortcut(updatedShortcut);

        // Assert
        Assert.True(connectionState.TryGetShortcut(originalShortcut.RouteGuid, out var retrieved));
        Assert.Equal(updatedShortcut.TargetGuid, retrieved!.TargetGuid);
        Assert.Equal(1, connectionState.ShortcutCount); // Still only one shortcut
    }

    /// <summary>
    /// Tests removing a specific shortcut by route GUID.
    /// </summary>
    [Fact]
    public void RemoveShortcut_ShouldRemoveByRouteGuid()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        var shortcut = CreateTestShortcut("join_game", "game-session");
        connectionState.AddOrUpdateShortcut(shortcut);

        // Act
        var removed = connectionState.RemoveShortcut(shortcut.RouteGuid);

        // Assert
        Assert.True(removed);
        Assert.False(connectionState.TryGetShortcut(shortcut.RouteGuid, out _));
        Assert.Equal(0, connectionState.ShortcutCount);
    }

    /// <summary>
    /// Tests that RemoveShortcut returns false for non-existent shortcuts.
    /// </summary>
    [Fact]
    public void RemoveShortcut_ShouldReturnFalseForNonExistent()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");

        // Act
        var removed = connectionState.RemoveShortcut(Guid.NewGuid());

        // Assert
        Assert.False(removed);
    }

    /// <summary>
    /// Tests bulk revocation of shortcuts by source service.
    /// </summary>
    [Fact]
    public void RemoveShortcutsByService_ShouldRemoveAllFromService()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        var shortcut1 = CreateTestShortcut("shortcut1", "game-session");
        var shortcut2 = CreateTestShortcut("shortcut2", "game-session");
        var shortcut3 = CreateTestShortcut("shortcut3", "auth-service"); // Different service

        connectionState.AddOrUpdateShortcut(shortcut1);
        connectionState.AddOrUpdateShortcut(shortcut2);
        connectionState.AddOrUpdateShortcut(shortcut3);

        // Act
        var removedCount = connectionState.RemoveShortcutsByService("game-session");

        // Assert
        Assert.Equal(2, removedCount);
        Assert.False(connectionState.TryGetShortcut(shortcut1.RouteGuid, out _));
        Assert.False(connectionState.TryGetShortcut(shortcut2.RouteGuid, out _));
        Assert.True(connectionState.TryGetShortcut(shortcut3.RouteGuid, out _)); // Still exists
        Assert.Equal(1, connectionState.ShortcutCount);
    }

    /// <summary>
    /// Tests ClearAllShortcuts removes all shortcuts.
    /// </summary>
    [Fact]
    public void ClearAllShortcuts_ShouldRemoveAll()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        connectionState.AddOrUpdateShortcut(CreateTestShortcut("s1", "svc1"));
        connectionState.AddOrUpdateShortcut(CreateTestShortcut("s2", "svc1"));
        connectionState.AddOrUpdateShortcut(CreateTestShortcut("s3", "svc2"));

        // Act
        connectionState.ClearAllShortcuts();

        // Assert
        Assert.Equal(0, connectionState.ShortcutCount);
    }

    /// <summary>
    /// Tests GetAllShortcuts returns a snapshot of all shortcuts.
    /// </summary>
    [Fact]
    public void GetAllShortcuts_ShouldReturnSnapshot()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        var shortcut1 = CreateTestShortcut("s1", "svc1");
        var shortcut2 = CreateTestShortcut("s2", "svc2");
        connectionState.AddOrUpdateShortcut(shortcut1);
        connectionState.AddOrUpdateShortcut(shortcut2);

        // Act
        var shortcuts = connectionState.GetAllShortcuts();

        // Assert
        Assert.Equal(2, shortcuts.Count);
        Assert.Contains(shortcuts, s => s.Name == "s1");
        Assert.Contains(shortcuts, s => s.Name == "s2");
    }

    /// <summary>
    /// Tests that expired shortcuts are identified correctly.
    /// </summary>
    [Fact]
    public void SessionShortcutData_IsExpired_ShouldReturnTrueForExpiredShortcuts()
    {
        // Arrange
        var expiredShortcut = new SessionShortcutData
        {
            RouteGuid = Guid.NewGuid(),
            TargetGuid = Guid.NewGuid(),
            SourceService = "test",
            Name = "expired",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) // Expired 1 hour ago
        };

        var validShortcut = new SessionShortcutData
        {
            RouteGuid = Guid.NewGuid(),
            TargetGuid = Guid.NewGuid(),
            SourceService = "test",
            Name = "valid",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) // Expires in 1 hour
        };

        var noExpiryShortcut = new SessionShortcutData
        {
            RouteGuid = Guid.NewGuid(),
            TargetGuid = Guid.NewGuid(),
            SourceService = "test",
            Name = "no_expiry",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = null // No expiration
        };

        // Assert
        Assert.True(expiredShortcut.IsExpired);
        Assert.False(validShortcut.IsExpired);
        Assert.False(noExpiryShortcut.IsExpired);
    }

    /// <summary>
    /// Tests thread-safety of concurrent shortcut operations.
    /// </summary>
    [Fact]
    public async Task ShortcutOperations_ShouldBeThreadSafe()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        var tasks = new List<Task>();
        var addedGuids = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        // Act - concurrent adds and removes
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var shortcut = CreateTestShortcut($"shortcut_{index}", "test-service");
                connectionState.AddOrUpdateShortcut(shortcut);
                addedGuids.Add(shortcut.RouteGuid);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - all shortcuts should be present
        Assert.Equal(100, connectionState.ShortcutCount);

        // Remove half in parallel
        var removeGuids = addedGuids.Take(50).ToList();
        var removeTasks = removeGuids.Select(guid => Task.Run(() => connectionState.RemoveShortcut(guid)));
        await Task.WhenAll(removeTasks);

        Assert.Equal(50, connectionState.ShortcutCount);
    }

    #endregion

    #region MessageRouter - Shortcut Routing Tests

    /// <summary>
    /// Tests that MessageRouter correctly identifies and routes shortcut messages.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_WithShortcut_ShouldReturnSessionShortcutRouteType()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);

        // Add a service mapping for the target
        var targetServiceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "game-session", _testServerSalt);
        connectionState.AddServiceMapping("game-session", targetServiceGuid);

        // Create shortcut pointing to that service
        var shortcutRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(sessionId, "join_game", "game-session", _testServerSalt);
        var shortcut = new SessionShortcutData
        {
            RouteGuid = shortcutRouteGuid,
            TargetGuid = targetServiceGuid,
            BoundPayload = new byte[] { 0x01, 0x02, 0x03 },
            SourceService = "game-session",
            Name = "join_game",
            TargetService = "game-session",
            TargetMethod = "POST",
            TargetEndpoint = "/sessions/join",
            CreatedAt = DateTimeOffset.UtcNow
        };
        connectionState.AddOrUpdateShortcut(shortcut);

        // Create message using the shortcut route GUID (client sends empty payload)
        var message = new BinaryMessage(
            MessageFlags.Binary,
            1, // channel
            1, // sequence
            shortcutRouteGuid, // Client uses shortcut GUID
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>() // Empty payload - will be injected
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.True(routeInfo.IsValid);
        Assert.Equal(RouteType.SessionShortcut, routeInfo.RouteType);
        Assert.Equal(targetServiceGuid, routeInfo.TargetGuid);
        Assert.Equal("game-session:POST:/sessions/join", routeInfo.ServiceName);
        Assert.Equal("join_game", routeInfo.ShortcutName);
        Assert.NotNull(routeInfo.InjectedPayload);
        Assert.Equal(3, routeInfo.InjectedPayload!.Length);
    }

    /// <summary>
    /// Tests that expired shortcuts return ShortcutExpired error.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_WithExpiredShortcut_ShouldReturnShortcutExpiredError()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);

        var targetServiceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "game-session", _testServerSalt);
        connectionState.AddServiceMapping("game-session", targetServiceGuid);

        var shortcutRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(sessionId, "expired_shortcut", "game-session", _testServerSalt);
        var expiredShortcut = new SessionShortcutData
        {
            RouteGuid = shortcutRouteGuid,
            TargetGuid = targetServiceGuid,
            BoundPayload = new byte[] { 0x01 },
            SourceService = "game-session",
            Name = "expired_shortcut",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) // Expired
        };
        connectionState.AddOrUpdateShortcut(expiredShortcut);

        var message = new BinaryMessage(
            MessageFlags.Binary,
            1, 1,
            shortcutRouteGuid,
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>()
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.False(routeInfo.IsValid);
        Assert.Equal(ResponseCodes.ShortcutExpired, routeInfo.ErrorCode);
        Assert.Contains("expired", routeInfo.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Expired shortcut should be removed from state
        Assert.False(connectionState.TryGetShortcut(shortcutRouteGuid, out _));
    }

    /// <summary>
    /// Tests that shortcuts without TargetService return ShortcutTargetNotFound error.
    /// Shortcuts MUST have TargetService set by the publisher - no fallback guessing.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_WithMissingTargetService_ShouldReturnShortcutTargetNotFoundError()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);

        // Create shortcut WITHOUT TargetService - this should be rejected
        var orphanTargetGuid = Guid.NewGuid();
        var shortcutRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(sessionId, "incomplete_shortcut", "some-service", _testServerSalt);
        var incompleteShortcut = new SessionShortcutData
        {
            RouteGuid = shortcutRouteGuid,
            TargetGuid = orphanTargetGuid,
            BoundPayload = new byte[] { 0x01 },
            SourceService = "some-service",
            // TargetService intentionally NOT set - this is an invalid shortcut
            Name = "incomplete_shortcut",
            CreatedAt = DateTimeOffset.UtcNow
        };
        connectionState.AddOrUpdateShortcut(incompleteShortcut);

        var message = new BinaryMessage(
            MessageFlags.Binary,
            1, 1,
            shortcutRouteGuid,
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>()
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert - shortcuts without TargetService are rejected
        Assert.False(routeInfo.IsValid);
        Assert.Equal(ResponseCodes.ShortcutTargetNotFound, routeInfo.ErrorCode);
        Assert.Contains("missing required target_service", routeInfo.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that shortcuts take priority over regular service GUID routing.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_ShortcutsCheckedBeforeServiceGuids()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);

        // This GUID could theoretically be both a service GUID and a shortcut route GUID
        // In practice, they use different version bits, but the routing logic should check shortcuts first
        var dualPurposeGuid = GuidGenerator.GenerateSessionShortcutGuid(sessionId, "priority_test", "test-service", _testServerSalt);

        // Add as both service mapping and shortcut (shouldn't happen, but tests priority)
        var targetGuid = GuidGenerator.GenerateServiceGuid(sessionId, "target-service", _testServerSalt);
        connectionState.AddServiceMapping("target-service", targetGuid);

        var shortcut = new SessionShortcutData
        {
            RouteGuid = dualPurposeGuid,
            TargetGuid = targetGuid,
            BoundPayload = new byte[] { 0xAB, 0xCD },
            SourceService = "test-service",
            TargetService = "target-service", // Required for routing
            TargetMethod = "POST", // Required for routing
            TargetEndpoint = "/test/endpoint", // Required for routing
            Name = "priority_test",
            CreatedAt = DateTimeOffset.UtcNow
        };
        connectionState.AddOrUpdateShortcut(shortcut);

        var message = new BinaryMessage(
            MessageFlags.Binary,
            1, 1,
            dualPurposeGuid,
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>()
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert - should be routed as shortcut, not service
        Assert.True(routeInfo.IsValid);
        Assert.Equal(RouteType.SessionShortcut, routeInfo.RouteType);
        Assert.NotNull(routeInfo.InjectedPayload);
    }

    /// <summary>
    /// Tests that shortcut routing preserves message metadata (channel, priority, etc.).
    /// </summary>
    [Fact]
    public void AnalyzeMessage_WithShortcut_ShouldPreserveMessageMetadata()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);

        var targetServiceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "game-session", _testServerSalt);
        connectionState.AddServiceMapping("game-session", targetServiceGuid);

        var shortcutRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(sessionId, "test_shortcut", "game-session", _testServerSalt);
        var shortcut = new SessionShortcutData
        {
            RouteGuid = shortcutRouteGuid,
            TargetGuid = targetServiceGuid,
            BoundPayload = new byte[] { 0x01 },
            SourceService = "game-session",
            TargetService = "game-session", // Required for routing
            TargetMethod = "POST", // Required for routing
            TargetEndpoint = "/sessions/join", // Required for routing
            Name = "test_shortcut",
            CreatedAt = DateTimeOffset.UtcNow
        };
        connectionState.AddOrUpdateShortcut(shortcut);

        var message = new BinaryMessage(
            MessageFlags.Binary | MessageFlags.Reserved0x08,
            42, // specific channel
            123, // specific sequence
            shortcutRouteGuid,
            0xDEADBEEF,
            Array.Empty<byte>()
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.True(routeInfo.IsValid);
        Assert.Equal(42, routeInfo.Channel);
        Assert.True(routeInfo.RequiresResponse); // Binary flag without Response flag means request
    }

    #endregion

    #region Helper Methods

    private SessionShortcutData CreateTestShortcut(string name, string sourceService)
    {
        var routeGuid = GuidGenerator.GenerateSessionShortcutGuid("test-session", name, sourceService, _testServerSalt);
        return new SessionShortcutData
        {
            RouteGuid = routeGuid,
            TargetGuid = Guid.NewGuid(),
            BoundPayload = new byte[] { 0x01, 0x02, 0x03 },
            SourceService = sourceService,
            Name = name,
            Description = $"Test shortcut: {name}",
            DisplayName = name.Replace("_", " "),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
