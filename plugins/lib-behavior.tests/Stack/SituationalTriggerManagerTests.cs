// =============================================================================
// Situational Trigger Manager Tests
// Tests for event and GOAP-driven behavior activation.
// =============================================================================

using BeyondImmersion.BannouService.Behavior.Stack;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Stack;

/// <summary>
/// Tests for <see cref="SituationalTriggerManager"/>.
/// </summary>
public sealed class SituationalTriggerManagerTests
{
    private readonly SituationalTriggerManager _manager;

    public SituationalTriggerManagerTests()
    {
        _manager = new SituationalTriggerManager();
    }

    // =========================================================================
    // REGISTRATION TESTS
    // =========================================================================

    [Fact]
    public void RegisterTrigger_EventTrigger_IndexesByEventName()
    {
        // Arrange
        var trigger = new SituationalTriggerDefinition(
            "enemy_spotted",
            "combat-mode",
            TriggerType.Event,
            Priority: 100);

        // Act
        _manager.RegisterTrigger(trigger);

        // Assert - fire the event to verify it's indexed
        var entityId = Guid.NewGuid();
        var requests = _manager.FireEvent(entityId, "enemy_spotted");
        Assert.Single(requests);
        Assert.Equal("combat-mode", requests[0].BehaviorId);
    }

    [Fact]
    public void RegisterTrigger_GoapTrigger_IndexesByGoalName()
    {
        // Arrange
        var trigger = new SituationalTriggerDefinition(
            "enter_vehicle",
            "vehicle-control",
            TriggerType.Goap,
            Priority: 50);

        // Act
        _manager.RegisterTrigger(trigger);

        // Assert - evaluate GOAP to verify it's indexed
        var entityId = Guid.NewGuid();
        var requests = _manager.EvaluateGoap(entityId, "enter_vehicle");
        Assert.Single(requests);
        Assert.Equal("vehicle-control", requests[0].BehaviorId);
    }

    // =========================================================================
    // EVENT TRIGGER TESTS
    // =========================================================================

    [Fact]
    public void FireEvent_UnknownEvent_ReturnsEmpty()
    {
        // Arrange
        var entityId = Guid.NewGuid();

        // Act
        var requests = _manager.FireEvent(entityId, "unknown_event");

        // Assert
        Assert.Empty(requests);
    }

    [Fact]
    public void FireEvent_WithCondition_EvaluatesCondition()
    {
        // Arrange
        var trigger = new SituationalTriggerDefinition(
            "test_event",
            "test-behavior",
            TriggerType.Event,
            Condition: ctx => ctx.EventData != null && ctx.EventData.ContainsKey("required_key"));

        _manager.RegisterTrigger(trigger);
        var entityId = Guid.NewGuid();

        // Act - fire without required data
        var requests1 = _manager.FireEvent(entityId, "test_event");

        // Act - fire with required data
        var data = new Dictionary<string, object> { ["required_key"] = "value" };
        var requests2 = _manager.FireEvent(entityId, "test_event", data);

        // Assert
        Assert.Empty(requests1); // Condition not met
        Assert.Single(requests2); // Condition met
    }

    [Fact]
    public void FireEvent_MultipleTriggers_ReturnsAll()
    {
        // Arrange
        _manager.RegisterTrigger(new SituationalTriggerDefinition(
            "combat_event", "behavior1", TriggerType.Event, 100));
        _manager.RegisterTrigger(new SituationalTriggerDefinition(
            "combat_event", "behavior2", TriggerType.Event, 50));

        var entityId = Guid.NewGuid();

        // Act
        var requests = _manager.FireEvent(entityId, "combat_event");

        // Assert
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public void FireEvent_WithDuration_SetsExpiresAt()
    {
        // Arrange
        var trigger = new SituationalTriggerDefinition(
            "timed_event",
            "timed-behavior",
            TriggerType.Event,
            Duration: TimeSpan.FromSeconds(5));

        _manager.RegisterTrigger(trigger);
        var entityId = Guid.NewGuid();

        // Act
        _manager.FireEvent(entityId, "timed_event");
        var activeTriggers = _manager.GetActiveTriggers(entityId);

        // Assert
        Assert.Single(activeTriggers);
        Assert.NotNull(activeTriggers[0].ExpiresAt);
        Assert.True(activeTriggers[0].ExpiresAt > DateTime.UtcNow);
    }

    // =========================================================================
    // GOAP TRIGGER TESTS
    // =========================================================================

    [Fact]
    public void EvaluateGoap_UnknownGoal_ReturnsEmpty()
    {
        // Arrange
        var entityId = Guid.NewGuid();

        // Act
        var requests = _manager.EvaluateGoap(entityId, "unknown_goal");

        // Assert
        Assert.Empty(requests);
    }

    [Fact]
    public void EvaluateGoap_KnownGoal_ReturnsTriggerRequest()
    {
        // Arrange
        var trigger = new SituationalTriggerDefinition(
            "mount_horse",
            "mounted-mode",
            TriggerType.Goap,
            Priority: 75);

        _manager.RegisterTrigger(trigger);
        var entityId = Guid.NewGuid();

        // Act
        var requests = _manager.EvaluateGoap(entityId, "mount_horse");

        // Assert
        Assert.Single(requests);
        Assert.Equal("mounted-mode", requests[0].BehaviorId);
        Assert.Equal(75, requests[0].Priority);
    }

    // =========================================================================
    // ACTIVE TRIGGER TESTS
    // =========================================================================

    [Fact]
    public void GetActiveTriggers_ReturnsOnlyNonExpired()
    {
        // Arrange
        _manager.RegisterTrigger(new SituationalTriggerDefinition(
            "event1", "behavior1", TriggerType.Event));
        _manager.RegisterTrigger(new SituationalTriggerDefinition(
            "event2", "behavior2", TriggerType.Event,
            Duration: TimeSpan.FromMilliseconds(1))); // Very short duration

        var entityId = Guid.NewGuid();
        _manager.FireEvent(entityId, "event1");
        _manager.FireEvent(entityId, "event2");

        // Wait for expiration
        Thread.Sleep(10);

        // Act
        var activeTriggers = _manager.GetActiveTriggers(entityId);

        // Assert
        Assert.Single(activeTriggers);
        Assert.Equal("event1", activeTriggers[0].Definition.TriggerId);
    }

    [Fact]
    public void DeactivateTrigger_RemovesTrigger()
    {
        // Arrange
        _manager.RegisterTrigger(new SituationalTriggerDefinition(
            "deactivate_test", "behavior", TriggerType.Event));

        var entityId = Guid.NewGuid();
        _manager.FireEvent(entityId, "deactivate_test");

        // Verify it's active
        Assert.Single(_manager.GetActiveTriggers(entityId));

        // Act
        var deactivated = _manager.DeactivateTrigger(entityId, "deactivate_test");

        // Assert
        Assert.True(deactivated);
        Assert.Empty(_manager.GetActiveTriggers(entityId));
    }

    [Fact]
    public void DeactivateTrigger_NonexistentTrigger_ReturnsFalse()
    {
        // Arrange
        var entityId = Guid.NewGuid();

        // Act
        var deactivated = _manager.DeactivateTrigger(entityId, "nonexistent");

        // Assert
        Assert.False(deactivated);
    }

    // =========================================================================
    // CLEANUP TESTS
    // =========================================================================

    [Fact]
    public void CleanupExpired_RemovesExpiredTriggers()
    {
        // Arrange
        _manager.RegisterTrigger(new SituationalTriggerDefinition(
            "cleanup_test", "behavior", TriggerType.Event,
            Duration: TimeSpan.FromMilliseconds(1)));

        var entityId = Guid.NewGuid();
        _manager.FireEvent(entityId, "cleanup_test");

        // Wait for expiration
        Thread.Sleep(10);

        // Act
        _manager.CleanupExpired();
        var activeTriggers = _manager.GetActiveTriggers(entityId);

        // Assert
        Assert.Empty(activeTriggers);
    }

    // =========================================================================
    // COMMON TRIGGERS TESTS
    // =========================================================================

    [Fact]
    public void CommonTriggers_All_ReturnsExpectedTriggers()
    {
        // Arrange & Act
        var all = CommonTriggers.All.ToList();

        // Assert
        Assert.NotEmpty(all);
        Assert.Contains(all, t => t.TriggerId == "enemy_spotted");
        Assert.Contains(all, t => t.TriggerId == "enter_vehicle");
        Assert.Contains(all, t => t.TriggerId == "conversation_started");
    }

    [Fact]
    public void CommonTriggers_CombatEntered_HasCorrectConfiguration()
    {
        // Arrange & Act
        var trigger = CommonTriggers.CombatEntered;

        // Assert
        Assert.Equal("enemy_spotted", trigger.TriggerId);
        Assert.Equal("combat-mode", trigger.BehaviorId);
        Assert.Equal(TriggerType.Event, trigger.Type);
        Assert.Equal(100, trigger.Priority);
    }
}
