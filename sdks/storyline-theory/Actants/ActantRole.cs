// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Actants;

/// <summary>
/// Greimas' Actantial Model - character role abstraction for templates.
/// This enables character-agnostic story templates where actions reference
/// roles (e.g., "Helper betrays Subject") rather than specific character IDs.
/// </summary>
/// <remarks>
/// The actantial model identifies six fundamental roles that appear across all narratives:
/// - Subject/Object axis: The quest relationship (who desires what)
/// - Sender/Receiver axis: The communication relationship (who initiates, who benefits)
/// - Helper/Opponent axis: The power relationship (who assists, who opposes)
///
/// Based on A.J. Greimas, "Structural Semantics" (1966).
/// </remarks>
public enum ActantRole
{
    /// <summary>
    /// The Subject - who desires and acts.
    /// Typically the protagonist who undertakes the quest.
    /// Examples: Hero, detective, lover, adventurer.
    /// </summary>
    Subject = 1,

    /// <summary>
    /// The Object - what is desired.
    /// The goal, person, or state the Subject seeks.
    /// Examples: Treasure, beloved, freedom, knowledge.
    /// </summary>
    Object = 2,

    /// <summary>
    /// The Sender - who initiates the quest.
    /// The force or character that sets the Subject on their path.
    /// Examples: King, mentor, prophecy, duty, moral imperative.
    /// </summary>
    Sender = 3,

    /// <summary>
    /// The Receiver - who benefits from quest completion.
    /// May be the Subject themselves, or another party.
    /// Examples: Kingdom, family, community, the Subject.
    /// </summary>
    Receiver = 4,

    /// <summary>
    /// The Helper - who assists the Subject.
    /// Provides aid, tools, information, or support.
    /// Examples: Sidekick, mentor, magic item, ally.
    /// </summary>
    Helper = 5,

    /// <summary>
    /// The Opponent - who opposes the Subject.
    /// Creates obstacles, challenges, or direct opposition.
    /// Examples: Villain, rival, monster, system, fate.
    /// </summary>
    Opponent = 6
}
