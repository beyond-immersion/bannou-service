// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Planning;
using BeyondImmersion.Bannou.StorylineTheory.Spectrums;

namespace BeyondImmersion.Bannou.StorylineTheory.Archives;

/// <summary>
/// Extracts narrative-relevant data from archives using opaque keys.
/// The SDK doesn't reference plugins directly - it receives a generic bundle
/// and extracts what it knows how to interpret.
/// </summary>
public sealed class ArchiveExtractor
{
    /// <summary>
    /// Extract WorldState from an archive bundle using opaque string keys.
    /// Unknown keys are silently ignored (forward compatibility).
    /// </summary>
    /// <param name="bundle">The archive bundle containing data entries.</param>
    /// <param name="primarySpectrum">The primary spectrum for the story.</param>
    /// <returns>The extracted world state.</returns>
    public WorldState ExtractWorldState(ArchiveBundle bundle, SpectrumType primarySpectrum)
    {
        var facts = new Dictionary<string, object>();

        // SDK uses opaque keys - doesn't know/care which plugin provides them
        // If entry exists and SDK knows how to interpret it, use it
        // If entry missing or unknown type, skip gracefully

        // Extraction rules for Generated archive types will be implemented
        // when the generation pipeline creates PersonalityArchiveData,
        // HistoryArchiveData, EncounterArchiveData, and RealmLoreArchiveData.

        return new WorldState
        {
            NarrativeState = CreateInitialNarrativeState(primarySpectrum),
            Facts = facts,
            Position = 0.0
        };
    }

    private static NarrativeState CreateInitialNarrativeState(SpectrumType primarySpectrum)
    {
        return new NarrativeState
        {
            PrimarySpectrum = primarySpectrum
        };
    }
}
