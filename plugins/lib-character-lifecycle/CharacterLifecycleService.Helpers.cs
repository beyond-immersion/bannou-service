namespace BeyondImmersion.BannouService.CharacterLifecycle;

// =============================================================================
// CharacterLifecycleService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by CharacterLifecycleService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (CharacterLifecycleService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ICharacterLifecycleService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (CharacterLifecycleService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for CharacterLifecycleService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class CharacterLifecycleService
{
    /// <summary>
    /// Decrements child count on a parent profile using ETag for concurrency.
    /// </summary>
    private async Task DecrementChildCountAsync(Guid parentId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.character-lifecycle", "CharacterLifecycleService.DecrementChildCount");
        var key = BuildProfileKey(parentId);
        var (parentProfile, etag) = await _profileStore.GetWithETagAsync(key, cancellationToken);
        if (parentProfile == null || etag == null) return;

        parentProfile.ChildCount = Math.Max(0, parentProfile.ChildCount - 1);
        parentProfile.UpdatedAt = DateTimeOffset.UtcNow;
        await _profileStore.TrySaveAsync(key, parentProfile, etag, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Recursively traverses ancestor generations via genetic profile parentAId/parentBId.
    /// </summary>
    private async Task TraverseAncestorsAsync(Guid characterId, int currentDepth, int maxDepth,
        List<FamilyTreeNode> nodes, HashSet<Guid> visited, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.character-lifecycle", "CharacterLifecycleService.TraverseAncestors");
        if (currentDepth > maxDepth) return;

        var genetic = await _geneticStore.GetAsync(BuildGeneticKey(characterId), cancellationToken);
        if (genetic == null) return;

        foreach (var parentId in new[] { genetic.ParentAId, genetic.ParentBId })
        {
            if (parentId == null || visited.Contains(parentId.Value)) continue;
            visited.Add(parentId.Value);

            var parentProfile = await _profileStore.GetAsync(BuildProfileKey(parentId.Value), cancellationToken);
            var parentGenetic = await _geneticStore.GetAsync(BuildGeneticKey(parentId.Value), cancellationToken);

            nodes.Add(new FamilyTreeNode
            {
                CharacterId = parentId.Value,
                SpeciesCode = parentProfile?.SpeciesCode,
                Generation = -currentDepth,
                Relationship = "ancestor",
                PhenotypeSummary = parentGenetic?.Phenotype.ToList(),
                BloodlineCodes = parentGenetic?.Bloodlines.Select(b => b.BloodlineCode).ToList()
            });

            await TraverseAncestorsAsync(parentId.Value, currentDepth + 1, maxDepth, nodes, visited, cancellationToken);
        }
    }

    /// <summary>
    /// Recursively traverses descendant generations by querying profiles where parentAId/parentBId matches.
    /// </summary>
    private async Task TraverseDescendantsAsync(Guid characterId, int currentDepth, int maxDepth,
        List<FamilyTreeNode> nodes, HashSet<Guid> visited, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.character-lifecycle", "CharacterLifecycleService.TraverseDescendants");
        if (currentDepth > maxDepth) return;

        var children = await _queryableProfileStore.QueryAsync(
            p => p.ParentAId == characterId || p.ParentBId == characterId, cancellationToken);

        foreach (var child in children)
        {
            if (visited.Contains(child.CharacterId)) continue;
            visited.Add(child.CharacterId);

            var childGenetic = await _geneticStore.GetAsync(BuildGeneticKey(child.CharacterId), cancellationToken);

            nodes.Add(new FamilyTreeNode
            {
                CharacterId = child.CharacterId,
                SpeciesCode = child.SpeciesCode,
                Generation = currentDepth,
                Relationship = "descendant",
                PhenotypeSummary = childGenetic?.Phenotype.ToList(),
                BloodlineCodes = childGenetic?.Bloodlines.Select(b => b.BloodlineCode).ToList()
            });

            await TraverseDescendantsAsync(child.CharacterId, currentDepth + 1, maxDepth, nodes, visited, cancellationToken);
        }
    }

}
