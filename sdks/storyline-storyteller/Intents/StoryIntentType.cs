// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Intents;

/// <summary>
/// Types of story intents that plugins dispatch to services.
/// </summary>
public enum StoryIntentType
{
    /// <summary>
    /// Spawn a new character.
    /// Plugin dispatches to ICharacterClient.CreateAsync().
    /// </summary>
    SpawnCharacter,

    /// <summary>
    /// Assign behavior to an actor.
    /// Plugin dispatches to IActorClient.UpdateBehaviorAsync().
    /// </summary>
    AssignBehavior,

    /// <summary>
    /// Create a contract between parties.
    /// Plugin dispatches to IContractClient.CreateFromTemplateAsync().
    /// </summary>
    CreateContract,

    /// <summary>
    /// Trigger an encounter between characters.
    /// Plugin dispatches to ICharacterEncounterClient.CreateAsync().
    /// </summary>
    TriggerEncounter,

    /// <summary>
    /// Modify a relationship between entities.
    /// Plugin dispatches to IRelationshipClient.UpdateAsync().
    /// </summary>
    ModifyRelationship,

    /// <summary>
    /// Update character personality traits.
    /// Plugin dispatches to ICharacterPersonalityClient.EvolveAsync().
    /// </summary>
    UpdatePersonality,

    /// <summary>
    /// Record a historical event participation.
    /// Plugin dispatches to ICharacterHistoryClient.AddParticipationAsync().
    /// </summary>
    RecordHistory
}
