using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.Actor.Tests;

/// <summary>
/// Unit tests for ActorState class - feelings, goals, memories, and change tracking.
/// </summary>
public class ActorStateTests
{
    #region Feeling State Tests

    [Fact]
    public void SetFeeling_UpdatesFeelingValue()
    {
        // Arrange
        var state = new ActorState();

        // Act
        state.SetFeeling("angry", 0.75);

        // Assert
        Assert.Equal(0.75, state.GetFeeling("angry"));
    }

    [Fact]
    public void SetFeeling_MarksStateAsChanged()
    {
        // Arrange
        var state = new ActorState();
        Assert.False(state.HasPendingChanges);

        // Act
        state.SetFeeling("fearful", 0.5);

        // Assert
        Assert.True(state.HasPendingChanges);
    }

    [Fact]
    public void GetFeeling_UnsetFeeling_ReturnsZero()
    {
        // Arrange
        var state = new ActorState();

        // Act
        var value = state.GetFeeling("nonexistent");

        // Assert
        Assert.Equal(0.0, value);
    }

    [Fact]
    public void SetFeeling_ClampsValueToValidRange()
    {
        // Arrange
        var state = new ActorState();

        // Act
        state.SetFeeling("happy", 1.5); // Above max
        state.SetFeeling("sad", -0.5); // Below min

        // Assert
        Assert.Equal(1.0, state.GetFeeling("happy"));
        Assert.Equal(0.0, state.GetFeeling("sad"));
    }

    [Fact]
    public void GetAllFeelings_ReturnsAllSetFeelings()
    {
        // Arrange
        var state = new ActorState();
        state.SetFeeling("angry", 0.3);
        state.SetFeeling("happy", 0.8);
        state.SetFeeling("alert", 0.5);

        // Act
        var feelings = state.GetAllFeelings();

        // Assert
        Assert.Equal(3, feelings.Count);
        Assert.Equal(0.3, feelings["angry"]);
        Assert.Equal(0.8, feelings["happy"]);
        Assert.Equal(0.5, feelings["alert"]);
    }

    [Fact]
    public void GetPendingFeelingChanges_ReturnsOnlyChangedFeelings()
    {
        // Arrange
        var state = new ActorState();
        state.SetFeeling("angry", 0.7);
        state.SetFeeling("happy", 0.3);

        // Act
        var changes = state.GetPendingFeelingChanges();

        // Assert
        Assert.NotNull(changes);
        Assert.True(Math.Abs(changes.Angry.GetValueOrDefault() - 0.7f) < 0.001f);
        Assert.True(Math.Abs(changes.Happy.GetValueOrDefault() - 0.3f) < 0.001f);
    }

    [Fact]
    public void GetPendingFeelingChanges_CustomFeelings_ReturnsInCustomDictionary()
    {
        // Arrange
        var state = new ActorState();
        state.SetFeeling("curiosity", 0.9);
        state.SetFeeling("excitement", 0.6);

        // Act
        var changes = state.GetPendingFeelingChanges();

        // Assert
        Assert.NotNull(changes);
        Assert.NotNull(changes.Custom);
        Assert.True(Math.Abs(changes.Custom["curiosity"] - 0.9f) < 0.001f);
        Assert.True(Math.Abs(changes.Custom["excitement"] - 0.6f) < 0.001f);
    }

    #endregion

    #region Goal State Tests

    [Fact]
    public void SetPrimaryGoal_UpdatesPrimaryGoal()
    {
        // Arrange
        var state = new ActorState();

        // Act
        state.SetPrimaryGoal("find_shelter");

        // Assert
        var goals = state.GetGoals();
        Assert.Equal("find_shelter", goals.PrimaryGoal);
    }

    [Fact]
    public void SetPrimaryGoal_WithParameters_StoresParameters()
    {
        // Arrange
        var state = new ActorState();
        var parameters = new Dictionary<string, object>
        {
            ["target_id"] = "entity-123",
            ["priority"] = 5
        };

        // Act
        state.SetPrimaryGoal("attack_target", parameters);

        // Assert
        var goals = state.GetGoals();
        Assert.Equal("attack_target", goals.PrimaryGoal);
        Assert.True(goals.GoalParameters.ContainsKey("target_id"));
        Assert.Equal("entity-123", goals.GoalParameters["target_id"]);
    }

    [Fact]
    public void AddSecondaryGoal_AddsToList()
    {
        // Arrange
        var state = new ActorState();
        state.SetPrimaryGoal("main_goal");

        // Act
        state.AddSecondaryGoal("gather_resources");
        state.AddSecondaryGoal("scout_area");

        // Assert
        var goals = state.GetGoals();
        Assert.Contains("gather_resources", goals.SecondaryGoals);
        Assert.Contains("scout_area", goals.SecondaryGoals);
    }

    [Fact]
    public void AddSecondaryGoal_DuplicateGoal_DoesNotAddTwice()
    {
        // Arrange
        var state = new ActorState();
        state.AddSecondaryGoal("patrol");
        state.AddSecondaryGoal("patrol"); // Duplicate

        // Assert
        var goals = state.GetGoals();
        Assert.Single(goals.SecondaryGoals);
    }

    [Fact]
    public void ClearGoals_ResetsAllGoals()
    {
        // Arrange
        var state = new ActorState();
        state.SetPrimaryGoal("important_goal", new Dictionary<string, object> { ["key"] = "value" });
        state.AddSecondaryGoal("secondary");

        // Act
        state.ClearGoals();

        // Assert
        var goals = state.GetGoals();
        Assert.Null(goals.PrimaryGoal);
        Assert.Empty(goals.GoalParameters);
        Assert.Empty(goals.SecondaryGoals);
    }

    [Fact]
    public void GetPendingGoalChanges_ReturnsGoalState()
    {
        // Arrange
        var state = new ActorState();
        state.SetPrimaryGoal("flee", new Dictionary<string, object> { ["from"] = "enemy" });
        state.AddSecondaryGoal("find_cover");

        // Act
        var changes = state.GetPendingGoalChanges();

        // Assert
        Assert.NotNull(changes);
        Assert.Equal("flee", changes.PrimaryGoal);
        Assert.NotNull(changes.GoalParameters); // Parameters were set
        Assert.Contains("find_cover", changes.SecondaryGoals ?? new List<string>());
    }

    #endregion

    #region Memory State Tests

    [Fact]
    public void AddMemory_AddsToMemoryList()
    {
        // Arrange
        var state = new ActorState();

        // Act
        state.AddMemory("friend_of", new { EntityId = "npc-456" });

        // Assert
        var memory = state.GetMemory("friend_of");
        Assert.NotNull(memory);
        Assert.Equal("friend_of", memory.MemoryKey);
    }

    [Fact]
    public void AddMemory_WithExpiration_SetsExpiresAt()
    {
        // Arrange
        var state = new ActorState();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        // Act
        state.AddMemory("temporary_memory", "some_value", expiresAt);

        // Assert
        var memory = state.GetMemory("temporary_memory");
        Assert.NotNull(memory);
        Assert.Equal(expiresAt, memory.ExpiresAt);
    }

    [Fact]
    public void AddMemory_SameKey_ReplacesExisting()
    {
        // Arrange
        var state = new ActorState();
        state.AddMemory("location", "place_a");

        // Act
        state.AddMemory("location", "place_b");

        // Assert
        var memory = state.GetMemory("location");
        Assert.Equal("place_b", memory?.MemoryValue);
        Assert.Single(state.GetAllMemories());
    }

    [Fact]
    public void RemoveMemory_RemovesFromList()
    {
        // Arrange
        var state = new ActorState();
        state.AddMemory("enemy_spotted", "enemy-123");

        // Act
        state.RemoveMemory("enemy_spotted");

        // Assert
        var memory = state.GetMemory("enemy_spotted");
        Assert.Null(memory);
    }

    [Fact]
    public void RemoveMemory_NonExistent_DoesNotThrow()
    {
        // Arrange
        var state = new ActorState();

        // Act & Assert - should not throw
        var exception = Record.Exception(() => state.RemoveMemory("nonexistent_key"));
        Assert.Null(exception);
    }

    [Fact]
    public void ModifyMemory_UpdatesExistingMemory()
    {
        // Arrange
        var state = new ActorState();
        state.AddMemory("trust_level", 50);
        state.ClearPendingChanges(); // Clear the add

        // Act
        state.ModifyMemory("trust_level", 75);

        // Assert
        var memory = state.GetMemory("trust_level");
        Assert.Equal(75, memory?.MemoryValue);
        Assert.True(state.HasPendingChanges);
    }

    [Fact]
    public void GetPendingMemoryChanges_ReturnsOnlyPendingUpdates()
    {
        // Arrange
        var state = new ActorState();
        state.AddMemory("new_memory", "value1");
        state.AddMemory("another_memory", "value2");

        // Act
        var changes = state.GetPendingMemoryChanges();

        // Assert
        Assert.NotNull(changes);
        Assert.Equal(2, changes.Count);
        Assert.Equal(MemoryUpdateOperation.Add, changes.First().Operation);
    }

    [Fact]
    public void CleanupExpiredMemories_RemovesExpiredOnly()
    {
        // Arrange
        var state = new ActorState();
        state.AddMemory("expired", "old", DateTimeOffset.UtcNow.AddMinutes(-1));
        state.AddMemory("valid", "current", DateTimeOffset.UtcNow.AddMinutes(5));
        state.AddMemory("permanent", "forever"); // No expiration

        // Act
        state.CleanupExpiredMemories();

        // Assert
        Assert.Null(state.GetMemory("expired"));
        Assert.NotNull(state.GetMemory("valid"));
        Assert.NotNull(state.GetMemory("permanent"));
    }

    #endregion

    #region Working Memory Tests

    [Fact]
    public void SetWorkingMemory_StoresValue()
    {
        // Arrange
        var state = new ActorState();

        // Act
        state.SetWorkingMemory("current_target", "entity-789");

        // Assert
        var value = state.GetWorkingMemory("current_target");
        Assert.Equal("entity-789", value);
    }

    [Fact]
    public void GetWorkingMemory_NonExistent_ReturnsNull()
    {
        // Arrange
        var state = new ActorState();

        // Act
        var value = state.GetWorkingMemory("nonexistent");

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void GetAllWorkingMemory_ReturnsAllEntries()
    {
        // Arrange
        var state = new ActorState();
        state.SetWorkingMemory("key1", "value1");
        state.SetWorkingMemory("key2", 42);
        state.SetWorkingMemory("key3", new { Data = "test" });

        // Act
        var all = state.GetAllWorkingMemory();

        // Assert
        Assert.Equal(3, all.Count);
        Assert.Equal("value1", all["key1"]);
        Assert.Equal(42, all["key2"]);
    }

    [Fact]
    public void ClearWorkingMemory_RemovesAllEntries()
    {
        // Arrange
        var state = new ActorState();
        state.SetWorkingMemory("key1", "value1");
        state.SetWorkingMemory("key2", "value2");

        // Act
        state.ClearWorkingMemory();

        // Assert
        Assert.Empty(state.GetAllWorkingMemory());
    }

    #endregion

    #region Behavior Change Tests

    [Fact]
    public void RecordBehaviorChange_SetsAddedBehaviors()
    {
        // Arrange
        var state = new ActorState();

        // Act
        state.RecordBehaviorChange(
            added: new[] { "new_skill_1", "new_skill_2" },
            removed: null,
            reason: "Leveled up");

        // Assert
        var change = state.GetPendingBehaviorChange();
        Assert.NotNull(change);
        Assert.Contains("new_skill_1", change.Added ?? new List<string>());
        Assert.Contains("new_skill_2", change.Added ?? new List<string>());
        Assert.Equal("Leveled up", change.Reason);
    }

    [Fact]
    public void RecordBehaviorChange_SetsRemovedBehaviors()
    {
        // Arrange
        var state = new ActorState();

        // Act
        state.RecordBehaviorChange(
            added: null,
            removed: new[] { "old_behavior" },
            reason: "Behavior forgotten");

        // Assert
        var change = state.GetPendingBehaviorChange();
        Assert.NotNull(change);
        Assert.Contains("old_behavior", change.Removed ?? new List<string>());
    }

    #endregion

    #region Change Tracking Tests

    [Fact]
    public void HasPendingChanges_NoModification_ReturnsFalse()
    {
        // Arrange
        var state = new ActorState();

        // Assert
        Assert.False(state.HasPendingChanges);
    }

    [Fact]
    public void HasPendingChanges_AfterFeelingChange_ReturnsTrue()
    {
        // Arrange
        var state = new ActorState();

        // Act
        state.SetFeeling("angry", 0.5);

        // Assert
        Assert.True(state.HasPendingChanges);
    }

    [Fact]
    public void HasPendingChanges_AfterGoalChange_ReturnsTrue()
    {
        // Arrange
        var state = new ActorState();

        // Act
        state.SetPrimaryGoal("new_goal");

        // Assert
        Assert.True(state.HasPendingChanges);
    }

    [Fact]
    public void HasPendingChanges_AfterMemoryChange_ReturnsTrue()
    {
        // Arrange
        var state = new ActorState();

        // Act
        state.AddMemory("key", "value");

        // Assert
        Assert.True(state.HasPendingChanges);
    }

    [Fact]
    public void HasPendingChanges_AfterBehaviorChange_ReturnsTrue()
    {
        // Arrange
        var state = new ActorState();

        // Act
        state.RecordBehaviorChange(new[] { "skill" }, null, "reason");

        // Assert
        Assert.True(state.HasPendingChanges);
    }

    [Fact]
    public void ClearPendingChanges_ResetsAllPendingState()
    {
        // Arrange
        var state = new ActorState();
        state.SetFeeling("angry", 0.5);
        state.SetPrimaryGoal("goal");
        state.AddMemory("key", "value");
        state.RecordBehaviorChange(new[] { "skill" }, null, "reason");

        // Act
        state.ClearPendingChanges();

        // Assert
        Assert.False(state.HasPendingChanges);
        Assert.Null(state.GetPendingFeelingChanges());
        Assert.Null(state.GetPendingGoalChanges());
        Assert.Null(state.GetPendingMemoryChanges());
        Assert.Null(state.GetPendingBehaviorChange());
    }

    [Fact]
    public void ClearPendingChanges_DoesNotAffectActualState()
    {
        // Arrange
        var state = new ActorState();
        state.SetFeeling("happy", 0.9);
        state.SetPrimaryGoal("persist_goal");
        state.AddMemory("persist_key", "persist_value");

        // Act
        state.ClearPendingChanges();

        // Assert - actual state should remain
        Assert.Equal(0.9, state.GetFeeling("happy"));
        Assert.Equal("persist_goal", state.GetGoals().PrimaryGoal);
        Assert.NotNull(state.GetMemory("persist_key"));
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentFeelingUpdates_ThreadSafe()
    {
        // Arrange
        var state = new ActorState();
        var tasks = new List<Task>();

        // Act - concurrent updates from multiple threads
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                state.SetFeeling($"feeling_{index % 10}", (index % 100) / 100.0);
            }));
        }
        await Task.WhenAll(tasks);

        // Assert - should not throw and should have consistent state
        var feelings = state.GetAllFeelings();
        Assert.NotNull(feelings);
        // At least some feelings should be present
        Assert.True(feelings.Count > 0);
    }

    [Fact]
    public async Task ConcurrentMemoryOperations_ThreadSafe()
    {
        // Arrange
        var state = new ActorState();
        var tasks = new List<Task>();

        // Act - concurrent add/remove/modify operations
        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                state.AddMemory($"memory_{index}", $"value_{index}");
            }));
        }
        await Task.WhenAll(tasks);

        // Assert - should not throw and memories should be present
        var memories = state.GetAllMemories();
        Assert.NotNull(memories);
        Assert.True(memories.Count > 0);
    }

    #endregion
}
