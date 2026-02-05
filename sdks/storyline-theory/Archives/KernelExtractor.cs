// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Archives;

/// <summary>
/// Identifies essential narrative events (kernels) from archive data.
/// Kernels are story opportunities - events that could seed new storylines.
/// </summary>
public sealed class KernelExtractor
{
    /// <summary>
    /// Extract narrative kernels from an archive bundle.
    /// Returns story opportunities ordered by significance.
    /// </summary>
    /// <param name="bundle">The archive bundle containing data entries.</param>
    /// <returns>List of narrative kernels sorted by significance (descending).</returns>
    public List<NarrativeKernel> ExtractKernels(ArchiveBundle bundle)
    {
        var kernels = new List<NarrativeKernel>();

        // Kernel extraction logic will be implemented when Generated archive types
        // are available from plugin schema generation pipeline:
        // - CharacterArchiveData for Death kernels
        // - HistoryArchiveData for HistoricalEvent, Trauma, UnfinishedBusiness kernels
        // - EncounterArchiveData for Conflict, DeepBond kernels

        return kernels.OrderByDescending(k => k.Significance).ToList();
    }
}
