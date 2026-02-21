// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineStoryteller.Planning;
using BeyondImmersion.Bannou.StorylineStoryteller.Templates;
using BeyondImmersion.Bannou.StorylineTheory.Planning;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Intents;

/// <summary>
/// Generates story-level intents from plan execution.
/// SDK defines intent vocabulary; plugin dispatches to appropriate service clients.
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
        // Implementation awaits integration with lib-storyline plugin.
        // Intent generation logic will map plan actions to service intents.
        return [];
    }
}
