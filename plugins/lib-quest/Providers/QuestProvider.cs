// =============================================================================
// Quest Variable Provider
// Provides quest data for ABML expressions via ${quest.*} paths.
// Owned by lib-quest per service hierarchy (L4).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Quest.Providers;

/// <summary>
/// Provides quest data for ABML expressions.
/// </summary>
/// <remarks>
/// <para>Variables available:</para>
/// <list type="bullet">
///   <item><description><c>${quest.active_count}</c> - int: Number of active quests</description></item>
///   <item><description><c>${quest.has_active}</c> - bool: Has any active quest?</description></item>
///   <item><description><c>${quest.codes}</c> - List: Active quest codes</description></item>
///   <item><description><c>${quest.active_quests}</c> - List: Active quest summaries</description></item>
///   <item><description><c>${quest.by_code.CODE.*}</c> - Quest details by code</description></item>
/// </list>
/// </remarks>
public sealed class QuestProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for non-character actors.
    /// </summary>
    public static QuestProvider Empty { get; } = new(new ListQuestsResponse { Quests = new List<QuestInstanceResponse>(), Total = 0 });

    private readonly List<QuestInstanceResponse> _quests;
    // Dictionary is readonly after construction - no need for ConcurrentDictionary
    private readonly Dictionary<string, QuestInstanceResponse> _byCode;

    /// <inheritdoc />
    public string Name => VariableProviderDefinitions.Quest;

    /// <summary>
    /// Creates a new QuestProvider with the given quest data.
    /// </summary>
    /// <param name="response">The quest list response. Use <see cref="Empty"/> for non-character actors.</param>
    public QuestProvider(ListQuestsResponse response)
    {
        _quests = response.Quests.ToList();
        _byCode = _quests.ToDictionary(q => q.Code, q => q, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        return path[0].ToLowerInvariant() switch
        {
            "active_count" => _quests.Count,
            "has_active" => _quests.Count > 0,
            "codes" => _quests.Select(q => q.Code).ToList(),
            "active_quests" => _quests.Select(QuestToDict).ToList(),
            "by_code" => path.Length > 1 ? GetQuestByCode(path.Slice(1)) : null,
            _ => null
        };
    }

    /// <inheritdoc />
    public object? GetRootValue()
    {
        return new Dictionary<string, object?>
        {
            ["active_count"] = _quests.Count,
            ["has_active"] = _quests.Count > 0,
            ["codes"] = _quests.Select(q => q.Code).ToList(),
            ["active_quests"] = _quests.Select(QuestToDict).ToList()
        };
    }

    /// <inheritdoc />
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        return path[0].ToLowerInvariant() switch
        {
            "active_count" or "has_active" or "codes" or "active_quests" => true,
            "by_code" => path.Length > 1,  // Requires quest code as next segment
            _ => false
        };
    }

    private object? GetQuestByCode(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return null;

        var code = path[0];
        if (!_byCode.TryGetValue(code, out var quest)) return null;

        if (path.Length == 1) return QuestToDict(quest);

        return path[1].ToLowerInvariant() switch
        {
            "status" => quest.Status.ToString(),  // Enum to string for ABML
            "name" => quest.Name,
            "progress" => CalculateOverallProgress(quest),
            "deadline" => quest.Deadline?.ToString("o"),  // Nullable - deadline may not exist
            "accepted_at" => quest.AcceptedAt.ToString("o"),
            "current_objective" => GetCurrentObjective(quest),
            "objectives" => quest.Objectives.Select(ObjectiveToDict).ToList(),
            _ => null
        };
    }

    private static float CalculateOverallProgress(QuestInstanceResponse quest)
    {
        if (quest.Objectives.Count == 0) return 0f;

        // Calculate average progress across all non-optional objectives
        var requiredObjectives = quest.Objectives.Where(o => !o.Optional).ToList();
        if (requiredObjectives.Count == 0)
        {
            // All objectives are optional - use all of them
            requiredObjectives = quest.Objectives.ToList();
        }

        return requiredObjectives.Sum(o => o.ProgressPercent) / requiredObjectives.Count;
    }

    private static Dictionary<string, object?>? GetCurrentObjective(QuestInstanceResponse quest)
    {
        // Find the first incomplete, non-hidden objective (the "current" one to focus on)
        var current = quest.Objectives.FirstOrDefault(o => !o.IsComplete && !o.Hidden);
        return current != null ? ObjectiveToDict(current) : null;
    }

    private static Dictionary<string, object?> QuestToDict(QuestInstanceResponse quest)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = quest.Code,
            ["name"] = quest.Name,
            ["status"] = quest.Status.ToString(),
            ["progress"] = CalculateOverallProgress(quest),
            ["deadline"] = quest.Deadline?.ToString("o"),  // Nullable - may not have deadline
            ["accepted_at"] = quest.AcceptedAt.ToString("o"),
            ["current_objective"] = GetCurrentObjective(quest)
        };
    }

    private static Dictionary<string, object?> ObjectiveToDict(ObjectiveProgress objective)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = objective.Code,
            ["name"] = objective.Name,
            ["description"] = objective.Description,  // Nullable per schema
            ["type"] = objective.ObjectiveType.ToString(),
            ["current_count"] = objective.CurrentCount,
            ["required_count"] = objective.RequiredCount,
            ["is_complete"] = objective.IsComplete,
            ["progress_percent"] = objective.ProgressPercent,
            ["hidden"] = objective.Hidden,
            ["optional"] = objective.Optional
        };
    }
}
