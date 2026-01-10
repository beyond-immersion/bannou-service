using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Mutable state container for actor behavioral/emotional state.
/// This tracks feelings, goals, memories - NOT physics (position, velocity).
/// Game servers handle physics; actors output intent/emotion that character behavior stacks read.
/// </summary>
public class ActorState
{
    private readonly Dictionary<string, double> _feelings = new();
    private readonly Dictionary<string, double> _pendingFeelingChanges = new();
    private GoalStateData _goals = new();
    private GoalStateData? _pendingGoalChanges;
    private readonly List<MemoryEntry> _memories = new();
    private readonly List<MemoryUpdateData> _pendingMemoryChanges = new();
    private BehaviorCompositionChangeData? _pendingBehaviorChange;
    private readonly Dictionary<string, object> _workingMemory = new();
    private readonly object _lock = new();

    /// <summary>
    /// Gets whether there are pending changes to publish.
    /// </summary>
    public bool HasPendingChanges
    {
        get
        {
            lock (_lock)
            {
                return _pendingFeelingChanges.Count > 0
                    || _pendingGoalChanges != null
                    || _pendingMemoryChanges.Count > 0
                    || _pendingBehaviorChange != null;
            }
        }
    }

    /// <summary>
    /// Gets a feeling value by name.
    /// </summary>
    /// <param name="name">The feeling name (e.g., "angry", "fearful").</param>
    /// <returns>The feeling intensity (0.0-1.0), or 0 if not set.</returns>
    public double GetFeeling(string name)
    {
        lock (_lock)
        {
            return _feelings.TryGetValue(name, out var value) ? value : 0.0;
        }
    }

    /// <summary>
    /// Sets a feeling value and marks it as a pending change.
    /// </summary>
    /// <param name="name">The feeling name.</param>
    /// <param name="value">The intensity (0.0-1.0).</param>
    public void SetFeeling(string name, double value)
    {
        lock (_lock)
        {
            var clampedValue = Math.Clamp(value, 0.0, 1.0);
            _feelings[name] = clampedValue;
            _pendingFeelingChanges[name] = clampedValue;
        }
    }

    /// <summary>
    /// Gets all current feelings.
    /// </summary>
    public IReadOnlyDictionary<string, double> GetAllFeelings()
    {
        lock (_lock)
        {
            return new Dictionary<string, double>(_feelings);
        }
    }

    /// <summary>
    /// Gets the current goal state.
    /// </summary>
    public GoalStateData GetGoals()
    {
        lock (_lock)
        {
            return new GoalStateData
            {
                PrimaryGoal = _goals.PrimaryGoal,
                GoalParameters = new Dictionary<string, object>(_goals.GoalParameters),
                SecondaryGoals = new List<string>(_goals.SecondaryGoals)
            };
        }
    }

    /// <summary>
    /// Sets the primary goal and marks it as a pending change.
    /// </summary>
    /// <param name="goal">The primary goal name.</param>
    /// <param name="parameters">Optional goal parameters.</param>
    public void SetPrimaryGoal(string goal, Dictionary<string, object>? parameters = null)
    {
        lock (_lock)
        {
            _goals.PrimaryGoal = goal;
            if (parameters != null)
            {
                _goals.GoalParameters = new Dictionary<string, object>(parameters);
            }
            _pendingGoalChanges = new GoalStateData
            {
                PrimaryGoal = _goals.PrimaryGoal,
                GoalParameters = new Dictionary<string, object>(_goals.GoalParameters),
                SecondaryGoals = new List<string>(_goals.SecondaryGoals)
            };
        }
    }

    /// <summary>
    /// Adds a secondary goal.
    /// </summary>
    /// <param name="goal">The secondary goal name.</param>
    public void AddSecondaryGoal(string goal)
    {
        lock (_lock)
        {
            if (!_goals.SecondaryGoals.Contains(goal))
            {
                _goals.SecondaryGoals.Add(goal);
                _pendingGoalChanges = new GoalStateData
                {
                    PrimaryGoal = _goals.PrimaryGoal,
                    GoalParameters = new Dictionary<string, object>(_goals.GoalParameters),
                    SecondaryGoals = new List<string>(_goals.SecondaryGoals)
                };
            }
        }
    }

    /// <summary>
    /// Clears all goals and marks as pending change.
    /// </summary>
    public void ClearGoals()
    {
        lock (_lock)
        {
            _goals = new GoalStateData();
            _pendingGoalChanges = new GoalStateData();
        }
    }

    /// <summary>
    /// Gets all current memories.
    /// </summary>
    public IReadOnlyList<MemoryEntry> GetAllMemories()
    {
        lock (_lock)
        {
            return _memories.ToList();
        }
    }

    /// <summary>
    /// Adds a memory and marks it as a pending change.
    /// </summary>
    /// <param name="key">The memory key.</param>
    /// <param name="value">The memory value.</param>
    /// <param name="expiresAt">Optional expiration time.</param>
    public void AddMemory(string key, object? value, DateTimeOffset? expiresAt = null)
    {
        lock (_lock)
        {
            // Remove existing memory with same key
            _memories.RemoveAll(m => m.MemoryKey == key);

            var entry = new MemoryEntry
            {
                MemoryKey = key,
                MemoryValue = value,
                ExpiresAt = expiresAt,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _memories.Add(entry);

            _pendingMemoryChanges.Add(new MemoryUpdateData
            {
                Operation = MemoryOperation.Add,
                MemoryKey = key,
                MemoryValue = value,
                ExpiresAt = expiresAt
            });
        }
    }

    /// <summary>
    /// Removes a memory and marks it as a pending change.
    /// </summary>
    /// <param name="key">The memory key to remove.</param>
    public void RemoveMemory(string key)
    {
        lock (_lock)
        {
            var removed = _memories.RemoveAll(m => m.MemoryKey == key);
            if (removed > 0)
            {
                _pendingMemoryChanges.Add(new MemoryUpdateData
                {
                    Operation = MemoryOperation.Remove,
                    MemoryKey = key
                });
            }
        }
    }

    /// <summary>
    /// Modifies an existing memory and marks it as a pending change.
    /// </summary>
    /// <param name="key">The memory key.</param>
    /// <param name="value">The new value.</param>
    public void ModifyMemory(string key, object? value)
    {
        lock (_lock)
        {
            var existing = _memories.FirstOrDefault(m => m.MemoryKey == key);
            if (existing != null)
            {
                existing.MemoryValue = value;
                _pendingMemoryChanges.Add(new MemoryUpdateData
                {
                    Operation = MemoryOperation.Modify,
                    MemoryKey = key,
                    MemoryValue = value
                });
            }
        }
    }

    /// <summary>
    /// Gets a memory by key.
    /// </summary>
    /// <param name="key">The memory key.</param>
    /// <returns>The memory entry if found, null otherwise.</returns>
    public MemoryEntry? GetMemory(string key)
    {
        lock (_lock)
        {
            return _memories.FirstOrDefault(m => m.MemoryKey == key);
        }
    }

    /// <summary>
    /// Records a behavior composition change (rare - only for learning/growth).
    /// </summary>
    /// <param name="added">Behavior IDs added.</param>
    /// <param name="removed">Behavior IDs removed.</param>
    /// <param name="reason">Reason for the change.</param>
    public void RecordBehaviorChange(IEnumerable<string>? added, IEnumerable<string>? removed, string? reason)
    {
        lock (_lock)
        {
            _pendingBehaviorChange = new BehaviorCompositionChangeData
            {
                Added = added?.ToList() ?? new List<string>(),
                Removed = removed?.ToList() ?? new List<string>(),
                Reason = reason
            };
        }
    }

    /// <summary>
    /// Sets a value in working memory.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void SetWorkingMemory(string key, object value)
    {
        lock (_lock)
        {
            _workingMemory[key] = value;
        }
    }

    /// <summary>
    /// Gets a value from working memory.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The value if found, null otherwise.</returns>
    public object? GetWorkingMemory(string key)
    {
        lock (_lock)
        {
            return _workingMemory.TryGetValue(key, out var value) ? value : null;
        }
    }

    /// <summary>
    /// Gets all working memory.
    /// </summary>
    public IReadOnlyDictionary<string, object> GetAllWorkingMemory()
    {
        lock (_lock)
        {
            return new Dictionary<string, object>(_workingMemory);
        }
    }

    /// <summary>
    /// Clears working memory.
    /// </summary>
    public void ClearWorkingMemory()
    {
        lock (_lock)
        {
            _workingMemory.Clear();
        }
    }

    /// <summary>
    /// Gets pending feeling changes for publishing.
    /// </summary>
    public FeelingState? GetPendingFeelingChanges()
    {
        lock (_lock)
        {
            if (_pendingFeelingChanges.Count == 0)
                return null;

            var state = new FeelingState();

            // Map standard feelings
            if (_pendingFeelingChanges.TryGetValue("angry", out var angry))
                state.Angry = (float)angry;
            if (_pendingFeelingChanges.TryGetValue("fearful", out var fearful))
                state.Fearful = (float)fearful;
            if (_pendingFeelingChanges.TryGetValue("happy", out var happy))
                state.Happy = (float)happy;
            if (_pendingFeelingChanges.TryGetValue("sad", out var sad))
                state.Sad = (float)sad;
            if (_pendingFeelingChanges.TryGetValue("alert", out var alert))
                state.Alert = (float)alert;

            // Map custom feelings
            var customFeelings = _pendingFeelingChanges
                .Where(kvp => !new[] { "angry", "fearful", "happy", "sad", "alert" }.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => (float)kvp.Value);

            if (customFeelings.Count > 0)
                state.Custom = customFeelings;

            return state;
        }
    }

    /// <summary>
    /// Gets pending goal changes for publishing.
    /// </summary>
    public GoalState? GetPendingGoalChanges()
    {
        lock (_lock)
        {
            if (_pendingGoalChanges == null)
                return null;

            return new GoalState
            {
                PrimaryGoal = _pendingGoalChanges.PrimaryGoal,
                GoalParameters = _pendingGoalChanges.GoalParameters.Count > 0
                    ? _pendingGoalChanges.GoalParameters
                    : null,
                SecondaryGoals = _pendingGoalChanges.SecondaryGoals.Count > 0
                    ? _pendingGoalChanges.SecondaryGoals
                    : null
            };
        }
    }

    /// <summary>
    /// Gets pending memory changes for publishing.
    /// </summary>
    public List<MemoryUpdate>? GetPendingMemoryChanges()
    {
        lock (_lock)
        {
            if (_pendingMemoryChanges.Count == 0)
                return null;

            return _pendingMemoryChanges.Select(m => new MemoryUpdate
            {
                Operation = m.Operation switch
                {
                    MemoryOperation.Add => MemoryUpdateOperation.Add,
                    MemoryOperation.Remove => MemoryUpdateOperation.Remove,
                    MemoryOperation.Modify => MemoryUpdateOperation.Modify,
                    _ => MemoryUpdateOperation.Add
                },
                MemoryKey = m.MemoryKey,
                MemoryValue = m.MemoryValue,
                ExpiresAt = m.ExpiresAt
            }).ToList();
        }
    }

    /// <summary>
    /// Gets pending behavior composition changes for publishing.
    /// </summary>
    public BehaviorCompositionChange? GetPendingBehaviorChange()
    {
        lock (_lock)
        {
            if (_pendingBehaviorChange == null)
                return null;

            return new BehaviorCompositionChange
            {
                Added = _pendingBehaviorChange.Added.Count > 0 ? _pendingBehaviorChange.Added : null,
                Removed = _pendingBehaviorChange.Removed.Count > 0 ? _pendingBehaviorChange.Removed : null,
                Reason = _pendingBehaviorChange.Reason
            };
        }
    }

    /// <summary>
    /// Clears all pending changes after publishing.
    /// </summary>
    public void ClearPendingChanges()
    {
        lock (_lock)
        {
            _pendingFeelingChanges.Clear();
            _pendingGoalChanges = null;
            _pendingMemoryChanges.Clear();
            _pendingBehaviorChange = null;
        }
    }

    /// <summary>
    /// Removes expired memories.
    /// </summary>
    public void CleanupExpiredMemories()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            _memories.RemoveAll(m => m.ExpiresAt.HasValue && m.ExpiresAt.Value <= now);
        }
    }
}

/// <summary>
/// Internal memory update data.
/// </summary>
internal class MemoryUpdateData
{
    public MemoryOperation Operation { get; set; }
    public string MemoryKey { get; set; } = string.Empty;
    public object? MemoryValue { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>
/// Internal memory operation enum.
/// </summary>
internal enum MemoryOperation
{
    Add,
    Remove,
    Modify
}

/// <summary>
/// Internal behavior composition change data.
/// </summary>
internal class BehaviorCompositionChangeData
{
    public List<string> Added { get; set; } = new();
    public List<string> Removed { get; set; } = new();
    public string? Reason { get; set; }
}
