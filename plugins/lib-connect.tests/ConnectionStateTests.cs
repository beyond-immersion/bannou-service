using BeyondImmersion.BannouService.Connect.Protocol;
using BeyondImmersion.BannouService.TestUtilities;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for ConnectionState non-shortcut functionality.
/// Tests service mappings, sequence numbers, pending messages,
/// rate limiting, and reconnection lifecycle.
/// Shortcut management is covered in SessionShortcutTests.
/// </summary>
public class ConnectionStateTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldInitializeAllProperties()
    {
        // Arrange & Act
        var before = DateTimeOffset.UtcNow;
        var state = new ConnectionState("test-session-123");
        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.Equal("test-session-123", state.SessionId);
        Assert.InRange(state.ConnectedAt, before, after);
        Assert.Equal(state.ConnectedAt, state.LastActivity);
        Assert.Empty(state.ServiceMappings);
        Assert.Empty(state.GuidMappings);
        Assert.Empty(state.ChannelSequences);
        Assert.Empty(state.PendingMessages);
        Assert.Empty(state.RateLimitTimestamps);
        Assert.Empty(state.SessionShortcuts);
        Assert.Empty(state.ShortcutsByService);
        Assert.Equal(ConnectionFlags.None, state.Flags);
        Assert.NotEqual(Guid.Empty, state.PeerGuid);
        Assert.Null(state.ReconnectionToken);
        Assert.Null(state.ReconnectionExpiresAt);
        Assert.Null(state.DisconnectedAt);
        Assert.Null(state.UserRoles);
    }

    [Fact]
    public void Constructor_ShouldGenerateUniquePeerGuids()
    {
        // Act
        var state1 = new ConnectionState("session-1");
        var state2 = new ConnectionState("session-2");

        // Assert
        Assert.NotEqual(state1.PeerGuid, state2.PeerGuid);
    }

    #endregion

    #region Service Mapping Tests

    [Fact]
    public void AddServiceMapping_ShouldAddBidirectionalMapping()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        var serviceGuid = Guid.NewGuid();

        // Act
        state.AddServiceMapping("account/get", serviceGuid);

        // Assert
        Assert.True(state.HasServiceMapping("account/get"));
        Assert.True(state.TryGetServiceName(serviceGuid, out var name));
        Assert.Equal("account/get", name);
    }

    [Fact]
    public void AddServiceMapping_ShouldOverwriteExistingMapping()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        state.AddServiceMapping("account/get", oldGuid);

        // Act
        state.AddServiceMapping("account/get", newGuid);

        // Assert
        Assert.True(state.HasServiceMapping("account/get"));
        Assert.True(state.TryGetServiceName(newGuid, out var name));
        Assert.Equal("account/get", name);
        // Old GUID should still be in GuidMappings (Dictionary doesn't auto-clean old entries)
        // This is acceptable because UpdateAllServiceMappings handles full rebuilds
    }

    [Fact]
    public void ClearServiceMappings_ShouldRemoveAll()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        state.AddServiceMapping("account/get", Guid.NewGuid());
        state.AddServiceMapping("auth/login", Guid.NewGuid());

        // Act
        state.ClearServiceMappings();

        // Assert
        Assert.False(state.HasServiceMapping("account/get"));
        Assert.False(state.HasServiceMapping("auth/login"));
        Assert.Empty(state.ServiceMappings);
        Assert.Empty(state.GuidMappings);
    }

    [Fact]
    public void UpdateAllServiceMappings_ShouldReplaceAllMappings()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        var oldGuid = Guid.NewGuid();
        state.AddServiceMapping("old/endpoint", oldGuid);

        var newGuid1 = Guid.NewGuid();
        var newGuid2 = Guid.NewGuid();
        var newMappings = new Dictionary<string, Guid>
        {
            ["new/endpoint1"] = newGuid1,
            ["new/endpoint2"] = newGuid2
        };

        // Act
        state.UpdateAllServiceMappings(newMappings);

        // Assert - old mapping gone
        Assert.False(state.HasServiceMapping("old/endpoint"));
        Assert.False(state.TryGetServiceName(oldGuid, out _));

        // New mappings present
        Assert.True(state.HasServiceMapping("new/endpoint1"));
        Assert.True(state.HasServiceMapping("new/endpoint2"));
        Assert.True(state.TryGetServiceName(newGuid1, out var name1));
        Assert.Equal("new/endpoint1", name1);
        Assert.True(state.TryGetServiceName(newGuid2, out var name2));
        Assert.Equal("new/endpoint2", name2);
    }

    [Fact]
    public void UpdateAllServiceMappings_WithEmptyDictionary_ShouldClearAll()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        state.AddServiceMapping("account/get", Guid.NewGuid());

        // Act
        state.UpdateAllServiceMappings(new Dictionary<string, Guid>());

        // Assert
        Assert.Empty(state.ServiceMappings);
        Assert.Empty(state.GuidMappings);
    }

    [Fact]
    public void HasServiceMapping_WhenNotExists_ShouldReturnFalse()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act & Assert
        Assert.False(state.HasServiceMapping("nonexistent/endpoint"));
    }

    [Fact]
    public void TryGetServiceName_WhenGuidNotExists_ShouldReturnFalse()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act
        var result = state.TryGetServiceName(Guid.NewGuid(), out var name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ServiceMappings_ShouldBeThreadSafe()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        var tasks = new List<Task>();

        // Act - concurrent reads and writes
        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                state.AddServiceMapping($"service/{index}", Guid.NewGuid());
            }));
            tasks.Add(Task.Run(() =>
            {
                state.HasServiceMapping($"service/{index}");
            }));
        }
        tasks.Add(Task.Run(() =>
        {
            state.ClearServiceMappings();
        }));
        tasks.Add(Task.Run(() =>
        {
            state.UpdateAllServiceMappings(new Dictionary<string, Guid>
            {
                ["final/endpoint"] = Guid.NewGuid()
            });
        }));

        // Assert - should not throw (thread-safety via locks)
        await Task.WhenAll(tasks);
    }

    #endregion

    #region Sequence Number Tests

    [Fact]
    public void GetNextSequenceNumber_ShouldStartAtOne()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act
        var seq = state.GetNextSequenceNumber(0);

        // Assert
        Assert.Equal(1u, seq);
    }

    [Fact]
    public void GetNextSequenceNumber_ShouldIncrementPerChannel()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act
        var seq1 = state.GetNextSequenceNumber(1);
        var seq2 = state.GetNextSequenceNumber(1);
        var seq3 = state.GetNextSequenceNumber(1);

        // Assert
        Assert.Equal(1u, seq1);
        Assert.Equal(2u, seq2);
        Assert.Equal(3u, seq3);
    }

    [Fact]
    public void GetNextSequenceNumber_ShouldTrackChannelsIndependently()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act
        var ch0_seq1 = state.GetNextSequenceNumber(0);
        var ch1_seq1 = state.GetNextSequenceNumber(1);
        var ch0_seq2 = state.GetNextSequenceNumber(0);
        var ch1_seq2 = state.GetNextSequenceNumber(1);

        // Assert - each channel has independent counter
        Assert.Equal(1u, ch0_seq1);
        Assert.Equal(1u, ch1_seq1);
        Assert.Equal(2u, ch0_seq2);
        Assert.Equal(2u, ch1_seq2);
    }

    [Fact]
    public async Task GetNextSequenceNumber_ShouldBeThreadSafe()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        var results = new System.Collections.Concurrent.ConcurrentBag<uint>();

        // Act - 100 concurrent increments on same channel
        var tasks = Enumerable.Range(0, 100).Select(_ =>
            Task.Run(() => results.Add(state.GetNextSequenceNumber(0))));
        await Task.WhenAll(tasks);

        // Assert - all 100 values should be unique (1 through 100)
        Assert.Equal(100, results.Distinct().Count());
        Assert.Equal(100, results.Count);
    }

    #endregion

    #region Pending Message Tests

    [Fact]
    public void AddPendingMessage_ShouldStoreMessageInfo()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        var sentAt = DateTimeOffset.UtcNow;

        // Act
        state.AddPendingMessage(42, "account", sentAt, 30);

        // Assert
        Assert.True(state.PendingMessages.ContainsKey(42));
        var info = state.PendingMessages[42];
        Assert.Equal("account", info.ServiceName);
        Assert.Equal(sentAt, info.SentAt);
        Assert.Equal(sentAt.AddSeconds(30), info.TimeoutAt);
    }

    [Fact]
    public void RemovePendingMessage_WhenExists_ShouldReturnInfo()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        state.AddPendingMessage(42, "account", DateTimeOffset.UtcNow);

        // Act
        var info = state.RemovePendingMessage(42);

        // Assert
        Assert.NotNull(info);
        Assert.Equal("account", info.ServiceName);
        Assert.Empty(state.PendingMessages);
    }

    [Fact]
    public void RemovePendingMessage_WhenNotExists_ShouldReturnNull()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act
        var info = state.RemovePendingMessage(999);

        // Assert
        Assert.Null(info);
    }

    [Fact]
    public void GetExpiredMessages_ShouldReturnOnlyExpired()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        var now = DateTimeOffset.UtcNow;

        // Add expired message (timeout 0 seconds = already expired)
        state.AddPendingMessage(1, "service-a", now.AddSeconds(-10), 5);
        // Add non-expired message
        state.AddPendingMessage(2, "service-b", now, 300);

        // Act
        var expired = state.GetExpiredMessages();

        // Assert
        Assert.Single(expired);
        Assert.Contains(1UL, expired);
        Assert.DoesNotContain(2UL, expired);
    }

    [Fact]
    public void GetExpiredMessages_WhenNoneExpired_ShouldReturnEmpty()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        state.AddPendingMessage(1, "service-a", DateTimeOffset.UtcNow, 300);

        // Act
        var expired = state.GetExpiredMessages();

        // Assert
        Assert.Empty(expired);
    }

    [Fact]
    public void GetExpiredMessages_WhenEmpty_ShouldReturnEmpty()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act
        var expired = state.GetExpiredMessages();

        // Assert
        Assert.Empty(expired);
    }

    #endregion

    #region Activity Tracking Tests

    [Fact]
    public void UpdateActivity_ShouldUpdateLastActivity()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        var initialActivity = state.LastActivity;

        // Small delay to ensure timestamp difference
        Thread.Sleep(10);

        // Act
        state.UpdateActivity();

        // Assert
        Assert.True(state.LastActivity > initialActivity);
    }

    #endregion

    #region Rate Limiting Tests

    [Fact]
    public void RecordMessageForRateLimit_ShouldAddTimestamp()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act
        state.RecordMessageForRateLimit();
        state.RecordMessageForRateLimit();
        state.RecordMessageForRateLimit();

        // Assert
        Assert.Equal(3, state.RateLimitTimestamps.Count);
    }

    [Fact]
    public void GetMessageCountInWindow_ShouldCountRecentMessages()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        state.RecordMessageForRateLimit();
        state.RecordMessageForRateLimit();
        state.RecordMessageForRateLimit();

        // Act - 1-minute window should include all recent messages
        var count = state.GetMessageCountInWindow(1);

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void GetMessageCountInWindow_ShouldExcludeOldMessages()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Manually enqueue old timestamps that are outside the window
        state.RateLimitTimestamps.Enqueue(DateTimeOffset.UtcNow.AddMinutes(-10));
        state.RateLimitTimestamps.Enqueue(DateTimeOffset.UtcNow.AddMinutes(-5));

        // Add a recent one
        state.RecordMessageForRateLimit();

        // Act - 1-minute window should only include the recent one
        var count = state.GetMessageCountInWindow(1);

        // Assert
        Assert.Equal(1, count);
        // Old entries should have been dequeued
        Assert.Single(state.RateLimitTimestamps);
    }

    [Fact]
    public void GetMessageCountInWindow_WithEmptyQueue_ShouldReturnZero()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act
        var count = state.GetMessageCountInWindow(1);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task RateLimiting_ShouldBeThreadSafe()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        var tasks = new List<Task>();

        // Act - concurrent recording and counting
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() => state.RecordMessageForRateLimit()));
            tasks.Add(Task.Run(() => state.GetMessageCountInWindow(1)));
        }

        // Assert - should not throw
        await Task.WhenAll(tasks);
    }

    #endregion

    #region Reconnection Lifecycle Tests

    [Fact]
    public void IsInReconnectionWindow_WhenNeverDisconnected_ShouldReturnFalse()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act & Assert
        Assert.False(state.IsInReconnectionWindow);
    }

    [Fact]
    public void InitiateReconnectionWindow_ShouldSetReconnectionState()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        var roles = new List<string> { "user", "admin" };

        // Act
        var before = DateTimeOffset.UtcNow;
        var token = state.InitiateReconnectionWindow(5, roles);
        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
        Assert.Equal(token, state.ReconnectionToken);
        Assert.NotNull(state.DisconnectedAt);
        Assert.InRange(state.DisconnectedAt.Value, before, after);
        Assert.NotNull(state.ReconnectionExpiresAt);
        // Expiry should be ~5 minutes after disconnect
        var expectedExpiry = state.DisconnectedAt.Value.AddMinutes(5);
        Assert.Equal(expectedExpiry, state.ReconnectionExpiresAt.Value);
        Assert.Equal(roles, state.UserRoles);
        Assert.True(state.IsInReconnectionWindow);
    }

    [Fact]
    public void InitiateReconnectionWindow_ShouldGenerateUniqueTokens()
    {
        // Arrange
        var state1 = new ConnectionState("session-1");
        var state2 = new ConnectionState("session-2");

        // Act
        var token1 = state1.InitiateReconnectionWindow();
        var token2 = state2.InitiateReconnectionWindow();

        // Assert
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void InitiateReconnectionWindow_WithDefaultMinutes_ShouldUse5Minutes()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act
        state.InitiateReconnectionWindow();

        // Assert
        var expected = state.DisconnectedAt!.Value.AddMinutes(5);
        Assert.Equal(expected, state.ReconnectionExpiresAt!.Value);
    }

    [Fact]
    public void InitiateReconnectionWindow_WithNullRoles_ShouldAcceptNull()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act
        state.InitiateReconnectionWindow(5, null);

        // Assert
        Assert.Null(state.UserRoles);
        Assert.True(state.IsInReconnectionWindow);
    }

    [Fact]
    public void ClearReconnectionState_ShouldResetReconnectionFields()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        var roles = new List<string> { "user" };
        state.InitiateReconnectionWindow(5, roles);

        // Act
        var beforeClear = DateTimeOffset.UtcNow;
        state.ClearReconnectionState();
        var afterClear = DateTimeOffset.UtcNow;

        // Assert
        Assert.Null(state.DisconnectedAt);
        Assert.Null(state.ReconnectionExpiresAt);
        Assert.Null(state.ReconnectionToken);
        Assert.False(state.IsInReconnectionWindow);
        // UserRoles are intentionally preserved per code comment
        Assert.Equal(roles, state.UserRoles);
        // LastActivity should be updated
        Assert.InRange(state.LastActivity, beforeClear, afterClear);
    }

    [Fact]
    public void IsInReconnectionWindow_WhenExpired_ShouldReturnFalse()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        // Initiate with 0 minutes to create already-expired window
        // (setting fields directly since InitiateReconnectionWindow uses UtcNow)
        state.DisconnectedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        state.ReconnectionExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Act & Assert
        Assert.False(state.IsInReconnectionWindow);
    }

    [Fact]
    public void IsInReconnectionWindow_WhenDisconnectedButNoExpiry_ShouldReturnFalse()
    {
        // Arrange
        var state = new ConnectionState("test-session");
        state.DisconnectedAt = DateTimeOffset.UtcNow;
        // ReconnectionExpiresAt is null

        // Act & Assert
        Assert.False(state.IsInReconnectionWindow);
    }

    #endregion

    #region ConnectionFlags Tests

    [Fact]
    public void Flags_ShouldSupportBitwiseOperations()
    {
        // Arrange
        var state = new ConnectionState("test-session");

        // Act
        state.Flags = ConnectionFlags.Authenticated | ConnectionFlags.CompressionEnabled;

        // Assert
        Assert.True(state.Flags.HasFlag(ConnectionFlags.Authenticated));
        Assert.True(state.Flags.HasFlag(ConnectionFlags.CompressionEnabled));
        Assert.False(state.Flags.HasFlag(ConnectionFlags.EncryptionEnabled));
        Assert.False(state.Flags.HasFlag(ConnectionFlags.HighPriorityAccess));
    }

    [Fact]
    public void Flags_ShouldDefaultToNone()
    {
        // Arrange & Act
        var state = new ConnectionState("test-session");

        // Assert
        Assert.Equal(ConnectionFlags.None, state.Flags);
    }

    #endregion
}
