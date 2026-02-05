// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Kernels;

namespace BeyondImmersion.Bannou.StorylineTheory.Archives;

/// <summary>
/// Identifies essential narrative events (kernels) from archive data.
/// Kernels are story opportunities - events that could seed new storylines.
/// </summary>
/// <remarks>
/// <para>
/// The KernelExtractor operates on ArchiveBundle data using opaque string keys.
/// It extracts kernels from known archive types (character, character-history,
/// character-encounter, character-personality) and processes nested archives recursively.
/// </para>
/// <para>
/// Kernel extraction logic will be fully implemented when Generated archive types
/// are available from the plugin schema generation pipeline (GH Issue #279).
/// </para>
/// </remarks>
public sealed class KernelExtractor
{
    private readonly KernelScorer _scorer = new();

    /// <summary>
    /// Extract narrative kernels from an archive bundle.
    /// Returns story opportunities ordered by significance.
    /// </summary>
    /// <param name="bundle">The archive bundle containing data entries.</param>
    /// <returns>List of narrative kernels sorted by significance (descending).</returns>
    public List<NarrativeKernel> ExtractKernels(ArchiveBundle bundle)
    {
        var kernels = new List<NarrativeKernel>();

        // Extract from each archive type the SDK knows about
        ExtractFromCharacterArchive(bundle, kernels);
        ExtractFromHistoryArchive(bundle, kernels);
        ExtractFromEncounterArchive(bundle, kernels);
        ExtractFromPersonalityArchive(bundle, kernels);

        // Process nested archives recursively
        foreach (var nested in bundle.GetNestedArchives())
        {
            kernels.AddRange(ExtractKernels(nested));
        }

        // Score and adjust significance based on cross-kernel analysis
        foreach (var kernel in kernels)
        {
            _scorer.AdjustSignificance(kernel, kernels);
        }

        return kernels
            .OrderByDescending(k => k.Significance)
            .ToList();
    }

    /// <summary>
    /// Extract Death kernels from character archive data.
    /// Death is always maximum significance for archived characters.
    /// </summary>
    private void ExtractFromCharacterArchive(ArchiveBundle bundle, List<NarrativeKernel> kernels)
    {
        // Implementation awaits Generated CharacterArchive type from GH Issue #279.
        // Will extract:
        // - Death kernel (if DeathDate is set) with cause, location, killer
        // - Significance = 1.0 (death is always maximum for archived characters)
    }

    /// <summary>
    /// Extract HistoricalEvent, Trauma, and UnfinishedBusiness kernels from history archive.
    /// </summary>
    private void ExtractFromHistoryArchive(ArchiveBundle bundle, List<NarrativeKernel> kernels)
    {
        // Implementation awaits Generated CharacterHistoryArchive type from GH Issue #279.
        // Will extract:
        // - HistoricalEvent kernels from high-significance event participations (> 0.7)
        // - Trauma kernels from backstory elements with ElementType == "trauma"
        // - UnfinishedBusiness kernels from unresolved goal backstory elements
    }

    /// <summary>
    /// Extract Conflict and DeepBond kernels from encounter archive.
    /// </summary>
    private void ExtractFromEncounterArchive(ArchiveBundle bundle, List<NarrativeKernel> kernels)
    {
        // Implementation awaits Generated CharacterEncounterArchive type from GH Issue #279.
        // Will extract:
        // - Conflict kernels from encounters with sentiment < -0.5 and impact > 0.7
        // - DeepBond kernels from relationships with sentiment > 0.6 and 5+ encounters
    }

    /// <summary>
    /// Personality archive doesn't directly produce kernels but can adjust scoring.
    /// </summary>
    private void ExtractFromPersonalityArchive(ArchiveBundle bundle, List<NarrativeKernel> kernels)
    {
        // Implementation awaits Generated CharacterPersonalityArchive type from GH Issue #279.
        // Personality traits can be used to adjust kernel significance:
        // - High confrontational trait boosts Conflict kernel significance
        // - High compassionate trait boosts DeepBond kernel significance
    }
}
