// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Arcs;

namespace BeyondImmersion.Bannou.StorylineTheory.Kernels;

/// <summary>
/// Adjusts kernel significance based on cross-kernel analysis.
/// Implements Lehnert's plot unit connectivity scoring for narrative potential.
/// </summary>
/// <remarks>
/// <para>
/// Kernels don't exist in isolation - their narrative potential depends on
/// connections to other kernels. A Death kernel connected to a Conflict kernel
/// (same characters involved) has higher story potential than an isolated death.
/// </para>
/// <para>
/// Based on Wendy Lehnert's plot unit theory which measures story coherence
/// through the connectivity of affect states.
/// </para>
/// </remarks>
public sealed class KernelScorer
{
    /// <summary>
    /// Adjust a kernel's significance based on its context within a kernel set.
    /// Call this after initial extraction to refine significance scores.
    /// </summary>
    /// <param name="kernel">The kernel to adjust.</param>
    /// <param name="allKernels">All kernels in the set for cross-reference.</param>
    /// <remarks>
    /// Adjustment factors include:
    /// - Character overlap with other kernels (shared story threads)
    /// - Causal potential (e.g., Conflict → Death sequence)
    /// - Temporal clustering (events close in time are more connected)
    /// - Genre coherence (kernels that fit the same genre boost each other)
    /// </remarks>
    public void AdjustSignificance(NarrativeKernel kernel, IReadOnlyList<NarrativeKernel> allKernels)
    {
        // Implementation awaits Generated archive types and full extraction logic.
        // Current stub preserves original significance scores.
        //
        // Future implementation will:
        // 1. Find kernels with overlapping InvolvedCharacterIds
        // 2. Boost significance when causal chains exist
        // 3. Apply genre coherence multipliers
        // 4. Cap adjusted significance at 1.0
    }

    /// <summary>
    /// Score the narrative potential of a kernel cluster.
    /// Higher scores indicate richer story possibilities when kernels are combined.
    /// </summary>
    /// <param name="kernels">The kernel cluster to evaluate.</param>
    /// <returns>
    /// Connectivity score from 0.0 to 1.0.
    /// Higher values indicate more interconnected kernels with greater narrative potential.
    /// </returns>
    /// <remarks>
    /// Based on Lehnert's plot unit theory: stories with high affect state
    /// connectivity feel more coherent and satisfying. This method measures
    /// how well a set of kernels could combine into a unified narrative.
    /// </remarks>
    public double ScoreKernelCluster(IEnumerable<NarrativeKernel> kernels)
    {
        var kernelList = kernels.ToList();
        if (kernelList.Count == 0)
        {
            return 0.0;
        }

        if (kernelList.Count == 1)
        {
            return kernelList[0].Significance;
        }

        // Future implementation will calculate:
        // 1. Character graph connectivity (shared characters between kernels)
        // 2. Type diversity bonus (multiple kernel types = richer story)
        // 3. Arc compatibility overlap (kernels that share compatible arcs)
        // 4. Genre coherence (kernels fitting the same genre patterns)

        // Stub: return average significance
        return kernelList.Average(k => k.Significance);
    }

    /// <summary>
    /// Find the best kernel subset for a given story goal.
    /// Selects kernels that maximize narrative potential for the target genre and arc.
    /// </summary>
    /// <param name="allKernels">All available kernels.</param>
    /// <param name="targetGenre">The desired genre (e.g., "action", "love", "horror").</param>
    /// <param name="targetArc">The desired emotional arc, or null for any arc.</param>
    /// <param name="maxKernels">Maximum number of kernels to select.</param>
    /// <returns>Ordered list of kernels best suited for the story goal.</returns>
    public IReadOnlyList<NarrativeKernel> SelectBestKernels(
        IEnumerable<NarrativeKernel> allKernels,
        string targetGenre,
        ArcType? targetArc,
        int maxKernels = 3)
    {
        var candidates = allKernels
            .Where(k => k.GenreAffinities.ContainsKey(targetGenre))
            .Where(k => targetArc == null || k.CompatibleArcs.Contains(targetArc.Value))
            .OrderByDescending(k => k.Significance * k.GenreAffinities.GetValueOrDefault(targetGenre, 0.5))
            .Take(maxKernels)
            .ToList();

        return candidates;
    }
}
