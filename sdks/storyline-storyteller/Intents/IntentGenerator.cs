// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Planning;
using BeyondImmersion.Bannou.StorylineStoryteller.Planning;
using BeyondImmersion.Bannou.StorylineStoryteller.Templates;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Intents;

/// <summary>
/// Generates story-level intents from plan execution.
/// </summary>
public sealed class IntentGenerator
{
    /// <summary>
    /// Generate intents from a storyline plan.
    /// </summary>
    /// <param name="plan">The storyline plan.</param>
    /// <param name="currentPhase">The current story phase.</param>
    /// <param name="currentState">The current world state.</param>
    /// <returns>Intents to be dispatched by the plugin.</returns>
    public IEnumerable<StorylineIntent> GenerateIntents(
        StorylinePlan plan,
        StoryPhase currentPhase,
        WorldState currentState)
    {
        var intents = new List<StorylineIntent>();

        // Generate intents based on planned actions
        foreach (var phase in plan.Phases)
        {
            foreach (var action in phase.Actions)
            {
                // Map action effects to intents
                var actionIntents = MapActionToIntents(action, currentState);
                intents.AddRange(actionIntents);
            }
        }

        return intents.OrderByDescending(i => i.Urgency);
    }

    private static IEnumerable<StorylineIntent> MapActionToIntents(
        StorylinePlanAction action,
        WorldState currentState)
    {
        // Basic intent mapping based on action effects
        // Full implementation would analyze action semantics more deeply

        var intents = new List<StorylineIntent>();

        foreach (var effect in action.Effects)
        {
            // Map effect keys to intent types
            var intent = MapEffectToIntent(effect.Key, effect.Value, action);
            if (intent != null)
            {
                intents.Add(intent);
            }
        }

        return intents;
    }

    private static StorylineIntent? MapEffectToIntent(string effectKey, object effectValue, StorylinePlanAction action)
    {
        // Pattern matching on effect keys to determine intent type
        // This is a simplified mapping - full implementation would be more sophisticated

        if (effectKey.Contains("encounter", StringComparison.OrdinalIgnoreCase))
        {
            return new StorylineIntent
            {
                Type = StoryIntentType.TriggerEncounter,
                Parameters = new Dictionary<string, object>
                {
                    ["action_id"] = action.ActionId,
                    ["effect_key"] = effectKey,
                    ["effect_value"] = effectValue
                },
                Urgency = action.IsCoreEvent ? 1.0 : 0.5,
                TargetRole = null
            };
        }

        if (effectKey.Contains("relationship", StringComparison.OrdinalIgnoreCase))
        {
            return new StorylineIntent
            {
                Type = StoryIntentType.ModifyRelationship,
                Parameters = new Dictionary<string, object>
                {
                    ["action_id"] = action.ActionId,
                    ["effect_key"] = effectKey,
                    ["effect_value"] = effectValue
                },
                Urgency = 0.6,
                TargetRole = null
            };
        }

        if (effectKey.Contains("behavior", StringComparison.OrdinalIgnoreCase) ||
            effectKey.Contains("goal", StringComparison.OrdinalIgnoreCase))
        {
            return new StorylineIntent
            {
                Type = StoryIntentType.AssignBehavior,
                Parameters = new Dictionary<string, object>
                {
                    ["action_id"] = action.ActionId,
                    ["effect_key"] = effectKey,
                    ["effect_value"] = effectValue
                },
                Urgency = 0.7,
                TargetRole = null
            };
        }

        // No matching intent type
        return null;
    }
}
