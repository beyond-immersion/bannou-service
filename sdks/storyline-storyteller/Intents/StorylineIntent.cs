// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Actants;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Intents;

/// <summary>
/// A story-level intent that plugins translate to service calls.
/// </summary>
public sealed class StorylineIntent
{
    /// <summary>
    /// The type of intent.
    /// </summary>
    public required StoryIntentType Type { get; init; }

    /// <summary>
    /// Parameters for the intent.
    /// </summary>
    public required Dictionary<string, object> Parameters { get; init; }

    /// <summary>
    /// Urgency affecting execution priority (0.0 to 1.0).
    /// </summary>
    public required double Urgency { get; init; }

    /// <summary>
    /// Which actant role this intent affects, if any.
    /// </summary>
    public required ActantRole? TargetRole { get; init; }
}
