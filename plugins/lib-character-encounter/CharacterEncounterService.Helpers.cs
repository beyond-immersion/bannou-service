using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.CharacterEncounter.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.CharacterEncounter;

// =============================================================================
// CharacterEncounterService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by CharacterEncounterService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (CharacterEncounterService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ICharacterEncounterService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (CharacterEncounterService.Helpers.cs):
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
/// Private and internal helper methods for CharacterEncounterService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class CharacterEncounterService
{
    // ============================================================================
    // Helper Methods
    // ============================================================================

    private async Task<EncounterTypeData> SeedBuiltInTypeAsync(IStateStore<EncounterTypeData> store, BuiltInEncounterType builtIn, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.SeedBuiltInTypeAsync");
        var data = new EncounterTypeData
        {
            TypeId = Guid.NewGuid(),
            Code = builtIn.Code,
            Name = builtIn.Name,
            Description = builtIn.Description,
            IsBuiltIn = true,
            DefaultEmotionalImpact = builtIn.DefaultEmotionalImpact,
            SortOrder = builtIn.SortOrder,
            IsActive = true,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await store.SaveAsync($"{TYPE_KEY_PREFIX}{builtIn.Code}", data, cancellationToken: cancellationToken);
        return data;
    }

    private async Task EnsureBuiltInTypesSeededAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.EnsureBuiltInTypesSeededAsync");
        if (!_configuration.SeedBuiltInTypesOnStartup) return;

        var store = _encounterTypeStore;
        foreach (var builtIn in BuiltInTypes)
        {
            var key = $"{TYPE_KEY_PREFIX}{builtIn.Code}";
            var existing = await store.GetAsync(key, cancellationToken);
            if (existing == null)
            {
                await SeedBuiltInTypeAsync(store, builtIn, cancellationToken);
            }
        }
    }

    private async Task<List<string>> GetAllTypeKeysAsync(IStateStore<EncounterTypeData> store, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetAllTypeKeysAsync");
        var keys = new List<string>();

        // Add built-in type keys
        foreach (var builtIn in BuiltInTypes)
        {
            keys.Add($"{TYPE_KEY_PREFIX}{builtIn.Code}");
        }

        // Add custom type keys from the index
        var customTypeIndexStore = _customTypeIndexStore;
        var customTypeIndex = await customTypeIndexStore.GetAsync(CUSTOM_TYPE_INDEX_KEY, cancellationToken);

        if (customTypeIndex != null)
        {
            foreach (var typeCode in customTypeIndex.TypeCodes)
            {
                keys.Add($"{TYPE_KEY_PREFIX}{typeCode}");
            }
        }

        return keys;
    }

    private async Task<List<Guid>> GetCharacterPerspectiveIdsAsync(Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetCharacterPerspectiveIdsAsync");
        var indexStore = _characterIndexStore;
        var index = await indexStore.GetAsync($"{CHAR_INDEX_PREFIX}{characterId}", cancellationToken);
        return index?.PerspectiveIds.ToList() ?? new List<Guid>();
    }

    private async Task<List<Guid>> GetPairEncounterIdsAsync(Guid charA, Guid charB, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetPairEncounterIdsAsync");
        var pairKey = BuildPairKey(charA, charB);
        var indexStore = _pairIndexStore;
        var index = await indexStore.GetAsync($"{PAIR_INDEX_PREFIX}{pairKey}", cancellationToken);
        return index?.EncounterIds.ToList() ?? new List<Guid>();
    }

    private async Task<List<Guid>> GetLocationEncounterIdsAsync(Guid locationId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetLocationEncounterIdsAsync");
        var indexStore = _locationIndexStore;
        var index = await indexStore.GetAsync($"{LOCATION_INDEX_PREFIX}{locationId}", cancellationToken);
        return index?.EncounterIds.ToList() ?? new List<Guid>();
    }

    /// <summary>
    /// Checks if a duplicate encounter already exists with the same participants, type, and timestamp within tolerance.
    /// </summary>
    private async Task<bool> IsDuplicateEncounterAsync(
        List<Guid> participantIds,
        string encounterTypeCode,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.IsDuplicateEncounterAsync");
        var toleranceMinutes = _configuration.DuplicateTimestampToleranceMinutes;
        var sortedParticipants = participantIds.OrderBy(id => id).ToList();
        var encounterStore = _encounterStore;

        // Collect candidate encounter IDs from pair indexes (any pair will do for duplicate check)
        var candidateEncounterIds = new HashSet<Guid>();

        // Check first pair only for efficiency - if a duplicate exists, it will be in this pair's index
        if (sortedParticipants.Count >= 2)
        {
            var pairEncounterIds = await GetPairEncounterIdsAsync(sortedParticipants[0], sortedParticipants[1], cancellationToken);
            foreach (var encounterId in pairEncounterIds)
            {
                candidateEncounterIds.Add(encounterId);
            }
        }

        // Check each candidate for duplicate match
        foreach (var encounterId in candidateEncounterIds)
        {
            var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
            if (encounter == null)
            {
                continue;
            }

            // Check type match
            if (!encounter.EncounterTypeCode.Equals(encounterTypeCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Check exact participant match (sorted comparison)
            var existingParticipants = encounter.ParticipantIds.OrderBy(id => id).ToList();
            if (!sortedParticipants.SequenceEqual(existingParticipants))
            {
                continue;
            }

            // Check timestamp within tolerance
            var existingTimestamp = DateTimeOffset.FromUnixTimeSeconds(encounter.Timestamp);
            var timeDiff = Math.Abs((timestamp - existingTimestamp).TotalMinutes);
            if (timeDiff <= toleranceMinutes)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Validates that all character IDs exist via the Character service.
    /// </summary>
    private async Task<bool> ValidateCharactersExistAsync(List<Guid> characterIds, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.ValidateCharactersExistAsync");
        var validationTasks = characterIds.Select(async characterId =>
        {
            try
            {
                await _characterClient.GetCharacterAsync(new GetCharacterRequest { CharacterId = characterId }, cancellationToken);
                return true;
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogWarning("Character {CharacterId} not found during encounter recording", characterId);
                return false;
            }
        });

        var results = await Task.WhenAll(validationTasks);
        return results.All(exists => exists);
    }

    private async Task AddToCharacterIndexAsync(Guid characterId, Guid perspectiveId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToCharacterIndexAsync");
        var indexStore = _characterIndexStore;
        var key = $"{CHAR_INDEX_PREFIX}{characterId}";

        for (var attempt = 0; attempt < _configuration.ETagRetryMaxAttempts; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            var isNewCharacter = index == null;

            index ??= new CharacterIndexData { CharacterId = characterId };

            if (!index.PerspectiveIds.Contains(perspectiveId))
            {
                index.PerspectiveIds.Add(perspectiveId);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken: cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on character index {CharacterId}, retrying (attempt {Attempt})",
                        characterId, attempt + 1);
                    continue;
                }
            }

            // Add to global character index if this is the character's first perspective
            if (isNewCharacter)
            {
                await AddToGlobalCharacterIndexAsync(characterId, cancellationToken);
            }

            return;
        }

        _logger.LogWarning("Failed to add perspective {PerspectiveId} to character index {CharacterId} after {MaxAttempts} attempts",
            perspectiveId, characterId, _configuration.ETagRetryMaxAttempts);
    }

    private async Task AddToGlobalCharacterIndexAsync(Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToGlobalCharacterIndexAsync");
        var globalIndexStore = _globalCharacterIndexStore;

        for (var attempt = 0; attempt < _configuration.ETagRetryMaxAttempts; attempt++)
        {
            var (globalIndex, etag) = await globalIndexStore.GetWithETagAsync(GLOBAL_CHAR_INDEX_KEY, cancellationToken);
            globalIndex ??= new GlobalCharacterIndexData();

            if (!globalIndex.CharacterIds.Contains(characterId))
            {
                globalIndex.CharacterIds.Add(characterId);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await globalIndexStore.TrySaveAsync(GLOBAL_CHAR_INDEX_KEY, globalIndex, etag ?? string.Empty, cancellationToken: cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on global character index, retrying (attempt {Attempt})", attempt + 1);
                    continue;
                }
            }

            return;
        }

        _logger.LogWarning("Failed to add character {CharacterId} to global character index after {MaxAttempts} attempts", characterId, _configuration.ETagRetryMaxAttempts);
    }

    private async Task RemoveFromCharacterIndexAsync(Guid characterId, Guid perspectiveId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromCharacterIndexAsync");
        var indexStore = _characterIndexStore;
        var key = $"{CHAR_INDEX_PREFIX}{characterId}";

        for (var attempt = 0; attempt < _configuration.ETagRetryMaxAttempts; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            if (index == null)
            {
                return;
            }

            index.PerspectiveIds.Remove(perspectiveId);
            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification on character index {CharacterId} during remove, retrying (attempt {Attempt})",
                    characterId, attempt + 1);
                continue;
            }

            // Remove from global index if this was the character's last perspective
            if (index.PerspectiveIds.Count == 0)
            {
                await RemoveFromGlobalCharacterIndexAsync(characterId, cancellationToken);
            }

            return;
        }

        _logger.LogWarning("Failed to remove perspective {PerspectiveId} from character index {CharacterId} after {MaxAttempts} attempts",
            perspectiveId, characterId, _configuration.ETagRetryMaxAttempts);
    }

    private async Task RemoveFromGlobalCharacterIndexAsync(Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromGlobalCharacterIndexAsync");

        var (result, _) = await _globalCharacterIndexStore.UpdateWithRetryAsync(
            GLOBAL_CHAR_INDEX_KEY,
            index => index.CharacterIds.Remove(characterId),
            _configuration.ETagRetryMaxAttempts,
            _logger,
            cancellationToken);

        if (result == UpdateResult.Conflict)
        {
            _logger.LogWarning("Failed to remove character {CharacterId} from global character index after {MaxAttempts} attempts", characterId, _configuration.ETagRetryMaxAttempts);
        }
    }

    private async Task AddToCustomTypeIndexAsync(string typeCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToCustomTypeIndexAsync");
        var indexStore = _customTypeIndexStore;

        for (var attempt = 0; attempt < _configuration.ETagRetryMaxAttempts; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(CUSTOM_TYPE_INDEX_KEY, cancellationToken);
            index ??= new CustomTypeIndexData();

            if (!index.TypeCodes.Contains(typeCode))
            {
                index.TypeCodes.Add(typeCode);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await indexStore.TrySaveAsync(CUSTOM_TYPE_INDEX_KEY, index, etag ?? string.Empty, cancellationToken: cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on custom type index, retrying (attempt {Attempt})", attempt + 1);
                    continue;
                }
            }

            return;
        }

        _logger.LogWarning("Failed to add type code {TypeCode} to custom type index after {MaxAttempts} attempts", typeCode, _configuration.ETagRetryMaxAttempts);
    }

    private async Task RemoveFromCustomTypeIndexAsync(string typeCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromCustomTypeIndexAsync");

        var (result, _) = await _customTypeIndexStore.UpdateWithRetryAsync(
            CUSTOM_TYPE_INDEX_KEY,
            index => index.TypeCodes.Remove(typeCode),
            _configuration.ETagRetryMaxAttempts,
            _logger,
            cancellationToken);

        if (result == UpdateResult.Conflict)
        {
            _logger.LogWarning("Failed to remove type code {TypeCode} from custom type index after {MaxAttempts} attempts", typeCode, _configuration.ETagRetryMaxAttempts);
        }
    }

    /// <summary>
    /// Adds an encounter ID to the type-encounter index for the given encounter type code.
    /// Uses optimistic concurrency with ETag-based retry pattern.
    /// </summary>
    private async Task AddToTypeEncounterIndexAsync(string typeCode, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToTypeEncounterIndexAsync");
        var indexStore = _typeEncounterIndexStore;
        var key = $"{TYPE_ENCOUNTER_INDEX_PREFIX}{typeCode}";

        for (var attempt = 0; attempt < _configuration.ETagRetryMaxAttempts; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            index ??= new TypeEncounterIndexData { TypeCode = typeCode };

            if (!index.EncounterIds.Contains(encounterId))
            {
                index.EncounterIds.Add(encounterId);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken: cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on type-encounter index {TypeCode}, retrying (attempt {Attempt})",
                        typeCode, attempt + 1);
                    continue;
                }
            }

            return;
        }

        _logger.LogWarning("Failed to add encounter {EncounterId} to type-encounter index {TypeCode} after {MaxAttempts} attempts",
            encounterId, typeCode, _configuration.ETagRetryMaxAttempts);
    }

    /// <summary>
    /// Removes an encounter ID from the type-encounter index for the given encounter type code.
    /// Uses optimistic concurrency with ETag-based retry pattern.
    /// </summary>
    private async Task RemoveFromTypeEncounterIndexAsync(string typeCode, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromTypeEncounterIndexAsync");
        var key = $"{TYPE_ENCOUNTER_INDEX_PREFIX}{typeCode}";

        var (result, _) = await _typeEncounterIndexStore.UpdateWithRetryAsync(
            key,
            index => index.EncounterIds.Remove(encounterId),
            _configuration.ETagRetryMaxAttempts,
            _logger,
            cancellationToken);

        if (result == UpdateResult.Conflict)
        {
            _logger.LogWarning("Failed to remove encounter {EncounterId} from type-encounter index {TypeCode} after {MaxAttempts} attempts",
                encounterId, typeCode, _configuration.ETagRetryMaxAttempts);
        }
    }

    /// <summary>
    /// Gets the count of encounters using the given encounter type code.
    /// </summary>
    private async Task<int> GetTypeEncounterCountAsync(string typeCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetTypeEncounterCountAsync");
        var indexStore = _typeEncounterIndexStore;
        var key = $"{TYPE_ENCOUNTER_INDEX_PREFIX}{typeCode}";
        var index = await indexStore.GetAsync(key, cancellationToken);
        return index?.EncounterIds.Count ?? 0;
    }

    /// <summary>
    /// Prunes encounters for a character if they exceed MaxEncountersPerCharacter.
    /// Removes oldest encounters (by timestamp) first.
    /// </summary>
    private async Task PruneCharacterEncountersIfNeededAsync(Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.PruneCharacterEncountersIfNeededAsync");
        var perspectiveIds = await GetCharacterPerspectiveIdsAsync(characterId, cancellationToken);
        if (perspectiveIds.Count <= _configuration.MaxEncountersPerCharacter)
        {
            return;
        }

        var perspectiveStore = _perspectiveStore;
        var encounterStore = _encounterStore;

        // Load all perspectives with their encounter timestamps
        var perspectivesWithTimestamp = new List<(Guid perspectiveId, Guid encounterId, long timestamp)>();
        foreach (var perspectiveId in perspectiveIds)
        {
            var perspective = await perspectiveStore.GetAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
            if (perspective == null) continue;

            var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{perspective.EncounterId}", cancellationToken);
            if (encounter == null) continue;

            perspectivesWithTimestamp.Add((perspectiveId, perspective.EncounterId, encounter.Timestamp));
        }

        // Sort by timestamp ascending (oldest first)
        var sorted = perspectivesWithTimestamp.OrderBy(p => p.timestamp).ToList();

        // Calculate how many to remove
        var toRemove = sorted.Count - _configuration.MaxEncountersPerCharacter;
        if (toRemove <= 0) return;

        _logger.LogInformation("Pruning {Count} oldest encounters for character {CharacterId}", toRemove, characterId);

        // Remove the oldest perspectives
        for (var i = 0; i < toRemove; i++)
        {
            var (perspectiveId, encounterId, _) = sorted[i];

            // Delete the perspective
            await perspectiveStore.DeleteAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
            await RemoveFromCharacterIndexAsync(characterId, perspectiveId, cancellationToken);

            // Check if this encounter has any remaining perspectives
            var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
            if (encounter != null)
            {
                var remainingPerspectives = await GetEncounterPerspectivesAsync(encounterId, cancellationToken);
                if (remainingPerspectives.Count == 0)
                {
                    // No perspectives left - delete the encounter
                    await encounterStore.DeleteAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);

                    // Clean up pair indexes
                    var participantIds = encounter.ParticipantIds;
                    await RemoveFromPairIndexesAsync(participantIds, encounterId, cancellationToken);

                    // Clean up location index
                    if (encounter.LocationId.HasValue)
                    {
                        await RemoveFromLocationIndexAsync(encounter.LocationId.Value, encounterId, cancellationToken);
                    }

                    // Clean up type-encounter index
                    await RemoveFromTypeEncounterIndexAsync(encounter.EncounterTypeCode, encounterId, cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Prunes encounters between character pairs if they exceed MaxEncountersPerPair.
    /// Removes oldest encounters (by timestamp) first.
    /// </summary>
    private async Task PrunePairEncountersIfNeededAsync(List<Guid> participantIds, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.PrunePairEncountersIfNeededAsync");
        var encounterStore = _encounterStore;

        // Check each unique pair
        for (var i = 0; i < participantIds.Count; i++)
        {
            for (var j = i + 1; j < participantIds.Count; j++)
            {
                var charA = participantIds[i];
                var charB = participantIds[j];

                var encounterIds = await GetPairEncounterIdsAsync(charA, charB, cancellationToken);
                if (encounterIds.Count <= _configuration.MaxEncountersPerPair)
                {
                    continue;
                }

                // Load encounters with timestamps
                var encountersWithTimestamp = new List<(Guid encounterId, long timestamp)>();
                foreach (var encounterId in encounterIds)
                {
                    var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
                    if (encounter != null)
                    {
                        encountersWithTimestamp.Add((encounterId, encounter.Timestamp));
                    }
                }

                // Sort by timestamp ascending (oldest first)
                var sorted = encountersWithTimestamp.OrderBy(e => e.timestamp).ToList();

                // Calculate how many to remove
                var toRemove = sorted.Count - _configuration.MaxEncountersPerPair;
                if (toRemove <= 0) continue;

                _logger.LogInformation("Pruning {Count} oldest encounters between pair {CharA}/{CharB}",
                    toRemove, charA, charB);

                // Remove the oldest encounters
                for (var k = 0; k < toRemove; k++)
                {
                    var (encounterId, _) = sorted[k];

                    // Delete all perspectives for this encounter
                    await DeleteEncounterPerspectivesAsync(encounterId, cancellationToken);

                    // Delete the encounter
                    var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
                    if (encounter != null)
                    {
                        await encounterStore.DeleteAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);

                        // Clean up pair indexes (including this pair and any others)
                        var allParticipants = encounter.ParticipantIds;
                        await RemoveFromPairIndexesAsync(allParticipants, encounterId, cancellationToken);

                        // Clean up location index
                        if (encounter.LocationId.HasValue)
                        {
                            await RemoveFromLocationIndexAsync(encounter.LocationId.Value, encounterId, cancellationToken);
                        }

                        // Clean up type-encounter index
                        await RemoveFromTypeEncounterIndexAsync(encounter.EncounterTypeCode, encounterId, cancellationToken);
                    }
                }
            }
        }
    }

    private async Task UpdatePairIndexesAsync(List<Guid> participantIds, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.UpdatePairIndexesAsync");
        var indexStore = _pairIndexStore;

        // Create pair indexes for each unique pair
        for (var i = 0; i < participantIds.Count; i++)
        {
            for (var j = i + 1; j < participantIds.Count; j++)
            {
                var pairKey = BuildPairKey(participantIds[i], participantIds[j]);
                var key = $"{PAIR_INDEX_PREFIX}{pairKey}";
                var charA = participantIds[i] < participantIds[j] ? participantIds[i] : participantIds[j];
                var charB = participantIds[i] < participantIds[j] ? participantIds[j] : participantIds[i];

                for (var attempt = 0; attempt < _configuration.ETagRetryMaxAttempts; attempt++)
                {
                    var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
                    index ??= new PairIndexData { CharacterIdA = charA, CharacterIdB = charB };

                    if (!index.EncounterIds.Contains(encounterId))
                    {
                        index.EncounterIds.Add(encounterId);
                        // etag is null when key doesn't exist yet; empty string signals
                        // "create new" to TrySaveAsync (will never conflict on new entries)
                        var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken: cancellationToken);
                        if (saveResult == null)
                        {
                            _logger.LogDebug("Concurrent modification on pair index {PairKey}, retrying (attempt {Attempt})",
                                pairKey, attempt + 1);
                            continue;
                        }
                    }

                    break;
                }
            }
        }
    }

    private async Task RemoveFromPairIndexesAsync(List<Guid> participantIds, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromPairIndexesAsync");

        for (var i = 0; i < participantIds.Count; i++)
        {
            for (var j = i + 1; j < participantIds.Count; j++)
            {
                var pairKey = BuildPairKey(participantIds[i], participantIds[j]);
                var key = $"{PAIR_INDEX_PREFIX}{pairKey}";

                await _pairIndexStore.UpdateWithRetryAsync(
                    key,
                    index => index.EncounterIds.Remove(encounterId),
                    _configuration.ETagRetryMaxAttempts,
                    _logger,
                    cancellationToken);
            }
        }
    }

    private async Task AddToLocationIndexAsync(Guid locationId, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToLocationIndexAsync");
        var indexStore = _locationIndexStore;
        var key = $"{LOCATION_INDEX_PREFIX}{locationId}";

        for (var attempt = 0; attempt < _configuration.ETagRetryMaxAttempts; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            index ??= new LocationIndexData { LocationId = locationId };

            if (!index.EncounterIds.Contains(encounterId))
            {
                index.EncounterIds.Add(encounterId);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken: cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on location index {LocationId}, retrying (attempt {Attempt})",
                        locationId, attempt + 1);
                    continue;
                }
            }

            return;
        }

        _logger.LogWarning("Failed to add encounter {EncounterId} to location index {LocationId} after {MaxAttempts} attempts",
            encounterId, locationId, _configuration.ETagRetryMaxAttempts);
    }

    private async Task RemoveFromLocationIndexAsync(Guid locationId, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromLocationIndexAsync");
        var key = $"{LOCATION_INDEX_PREFIX}{locationId}";

        var (result, _) = await _locationIndexStore.UpdateWithRetryAsync(
            key,
            index => index.EncounterIds.Remove(encounterId),
            _configuration.ETagRetryMaxAttempts,
            _logger,
            cancellationToken);

        if (result == UpdateResult.Conflict)
        {
            _logger.LogWarning("Failed to remove encounter {EncounterId} from location index {LocationId} after {MaxAttempts} attempts",
                encounterId, locationId, _configuration.ETagRetryMaxAttempts);
        }
    }

    // ============================================================================
    // Encounter-Perspective Index Management
    // ============================================================================

    private async Task AddToEncounterPerspectiveIndexAsync(Guid encounterId, Guid perspectiveId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToEncounterPerspectiveIndexAsync");
        var indexStore = _encounterPerspectiveIndexStore;
        var key = $"{ENCOUNTER_PERSPECTIVE_INDEX_PREFIX}{encounterId}";

        for (var attempt = 0; attempt < _configuration.ETagRetryMaxAttempts; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            index ??= new EncounterPerspectiveIndexData { EncounterId = encounterId };

            if (!index.PerspectiveIds.Contains(perspectiveId))
            {
                index.PerspectiveIds.Add(perspectiveId);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken: cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on encounter perspective index {EncounterId}, retrying (attempt {Attempt})",
                        encounterId, attempt + 1);
                    continue;
                }
            }
            return;
        }

        _logger.LogWarning("Failed to add perspective {PerspectiveId} to encounter perspective index {EncounterId} after {MaxAttempts} attempts",
            perspectiveId, encounterId, _configuration.ETagRetryMaxAttempts);
    }

    private async Task RemoveFromEncounterPerspectiveIndexAsync(Guid encounterId, Guid perspectiveId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromEncounterPerspectiveIndexAsync");
        var indexStore = _encounterPerspectiveIndexStore;
        var key = $"{ENCOUNTER_PERSPECTIVE_INDEX_PREFIX}{encounterId}";

        for (var attempt = 0; attempt < _configuration.ETagRetryMaxAttempts; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            if (index == null) return;

            index.PerspectiveIds.Remove(perspectiveId);

            if (index.PerspectiveIds.Count == 0)
            {
                // Delete empty index
                await indexStore.DeleteAsync(key, cancellationToken);
                return;
            }

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification on encounter perspective index {EncounterId}, retrying (attempt {Attempt})",
                    encounterId, attempt + 1);
                continue;
            }
            return;
        }

        _logger.LogWarning("Failed to remove perspective {PerspectiveId} from encounter perspective index {EncounterId} after {MaxAttempts} attempts",
            perspectiveId, encounterId, _configuration.ETagRetryMaxAttempts);
    }

    private async Task DeleteEncounterPerspectiveIndexAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.DeleteEncounterPerspectiveIndexAsync");
        var indexStore = _encounterPerspectiveIndexStore;
        var key = $"{ENCOUNTER_PERSPECTIVE_INDEX_PREFIX}{encounterId}";
        await indexStore.DeleteAsync(key, cancellationToken);
    }

    private async Task<List<Guid>> GetEncounterPerspectiveIdsAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetEncounterPerspectiveIdsAsync");
        var indexStore = _encounterPerspectiveIndexStore;
        var key = $"{ENCOUNTER_PERSPECTIVE_INDEX_PREFIX}{encounterId}";
        var index = await indexStore.GetAsync(key, cancellationToken);
        return index?.PerspectiveIds ?? new List<Guid>();
    }

    // ============================================================================
    // Bulk Load Helpers
    // ============================================================================

    /// <summary>
    /// Bulk loads perspectives by IDs with parallel lazy decay.
    /// </summary>
    private async Task<List<PerspectiveData>> BulkLoadPerspectivesWithDecayAsync(
        IEnumerable<Guid> perspectiveIds,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.BulkLoadPerspectivesWithDecayAsync");
        var idList = perspectiveIds.ToList();
        if (idList.Count == 0) return new List<PerspectiveData>();

        var perspectiveStore = _perspectiveStore;
        var perspectiveKeys = idList.Select(id => $"{PERSPECTIVE_KEY_PREFIX}{id}").ToList();

        // Single bulk read
        var bulkResult = await perspectiveStore.GetBulkAsync(perspectiveKeys, cancellationToken);

        if (!_configuration.MemoryDecayEnabled || _configuration.MemoryDecayMode != MemoryDecayMode.Lazy)
        {
            // No decay needed - just return values
            return bulkResult.Values.Where(p => p != null).Cast<PerspectiveData>().ToList();
        }

        // When using game-time, we cannot cheaply pre-filter (need encounter realmId per perspective).
        // When using real-time, we can pre-filter with the lightweight CalculateRealTimeDecay check.
        // ApplyLazyDecayAsync handles both modes and short-circuits internally when no decay is needed.
        var allPerspectives = bulkResult.Values.Where(p => p != null).Cast<PerspectiveData>().ToList();

        if (_configuration.DecayTimeSource == TimeSource.RealTime)
        {
            // Real-time: pre-filter to avoid unnecessary re-fetches for perspectives that don't need decay
            var needsDecay = new List<PerspectiveData>();
            var noDecayNeeded = new List<PerspectiveData>();

            foreach (var perspective in allPerspectives)
            {
                var (shouldDecay, _) = CalculateRealTimeDecay(perspective);
                if (shouldDecay)
                    needsDecay.Add(perspective);
                else
                    noDecayNeeded.Add(perspective);
            }

            if (needsDecay.Count == 0)
                return noDecayNeeded;

            var decayTasks = needsDecay.Select(p => ApplyLazyDecayAsync(perspectiveStore, p, cancellationToken));
            var decayedPerspectives = await Task.WhenAll(decayTasks);
            return noDecayNeeded.Concat(decayedPerspectives).ToList();
        }

        // Game-time: pass all perspectives through ApplyLazyDecayAsync (handles encounter loading and Worldstate calls)
        var gameTimeDecayTasks = allPerspectives.Select(p => ApplyLazyDecayAsync(perspectiveStore, p, cancellationToken));
        var gameTimeResults = await Task.WhenAll(gameTimeDecayTasks);
        return gameTimeResults.ToList();
    }

    /// <summary>
    /// Bulk loads encounters by IDs.
    /// </summary>
    private async Task<Dictionary<Guid, EncounterData>> BulkLoadEncountersAsync(
        IEnumerable<Guid> encounterIds,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.BulkLoadEncountersAsync");
        var idList = encounterIds.ToList();
        if (idList.Count == 0) return new Dictionary<Guid, EncounterData>();

        var encounterStore = _encounterStore;
        var encounterKeys = idList.Select(id => $"{ENCOUNTER_KEY_PREFIX}{id}").ToList();

        var bulkResult = await encounterStore.GetBulkAsync(encounterKeys, cancellationToken);

        // Map back to encounter IDs
        var result = new Dictionary<Guid, EncounterData>();
        foreach (var id in idList)
        {
            var key = $"{ENCOUNTER_KEY_PREFIX}{id}";
            if (bulkResult.TryGetValue(key, out var encounter) && encounter != null)
            {
                result[id] = encounter;
            }
        }
        return result;
    }

    /// <summary>
    /// Bulk loads all perspectives for multiple encounters at once.
    /// Uses parallel index lookups followed by a single bulk perspective load.
    /// </summary>
    private async Task<Dictionary<Guid, List<EncounterPerspectiveModel>>> BulkLoadAllEncounterPerspectivesAsync(
        IEnumerable<Guid> encounterIds,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.BulkLoadAllEncounterPerspectivesAsync");
        var idList = encounterIds.ToList();
        if (idList.Count == 0) return new Dictionary<Guid, List<EncounterPerspectiveModel>>();

        // Step 1: Parallel fetch perspective IDs for all encounters
        var indexTasks = idList.Select(async encounterId =>
        {
            var perspectiveIds = await GetEncounterPerspectiveIdsAsync(encounterId, cancellationToken);
            return (encounterId, perspectiveIds);
        });
        var indexResults = await Task.WhenAll(indexTasks);

        // Build mapping: perspectiveId -> encounterId and collect all perspective IDs
        var perspectiveToEncounter = new Dictionary<Guid, Guid>();
        var allPerspectiveIds = new List<Guid>();
        var encountersThatNeedLegacyFallback = new List<Guid>();

        foreach (var (encounterId, perspectiveIds) in indexResults)
        {
            if (perspectiveIds.Count > 0)
            {
                foreach (var perspectiveId in perspectiveIds)
                {
                    perspectiveToEncounter[perspectiveId] = encounterId;
                    allPerspectiveIds.Add(perspectiveId);
                }
            }
            else
            {
                // No index entry - will need legacy fallback
                encountersThatNeedLegacyFallback.Add(encounterId);
            }
        }

        // Step 2: Single bulk load of all perspectives with parallel decay
        var allPerspectives = await BulkLoadPerspectivesWithDecayAsync(allPerspectiveIds, cancellationToken);

        // Step 3: Group perspectives by encounter ID
        var result = new Dictionary<Guid, List<EncounterPerspectiveModel>>();
        foreach (var perspective in allPerspectives)
        {
            if (perspectiveToEncounter.TryGetValue(perspective.PerspectiveId, out var encounterId))
            {
                if (!result.ContainsKey(encounterId))
                {
                    result[encounterId] = new List<EncounterPerspectiveModel>();
                }
                result[encounterId].Add(MapToPerspectiveModel(perspective));
            }
        }

        // Step 4: Handle legacy fallback for encounters without index (pre-existing data)
        if (encountersThatNeedLegacyFallback.Count > 0)
        {
            foreach (var encounterId in encountersThatNeedLegacyFallback)
            {
                var legacyPerspectives = await GetEncounterPerspectivesAsync(encounterId, cancellationToken);
                if (legacyPerspectives.Count > 0)
                {
                    result[encounterId] = legacyPerspectives;
                }
            }
        }

        return result;
    }

    internal static string BuildPairKey(Guid charA, Guid charB)
    {
        // Always put the smaller GUID first for consistent keying
        return charA < charB ? $"{charA}:{charB}" : $"{charB}:{charA}";
    }

    /// <summary>
    /// Builds the key for an encounter record.
    /// Format: {ENCOUNTER_KEY_PREFIX}{encounterId}
    /// </summary>
    internal static string BuildEncounterKey(Guid encounterId)
        => $"{ENCOUNTER_KEY_PREFIX}{encounterId}";

    /// <summary>
    /// Builds the key for a perspective record.
    /// Format: {PERSPECTIVE_KEY_PREFIX}{perspectiveId}
    /// </summary>
    internal static string BuildPerspectiveKey(Guid perspectiveId)
        => $"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}";

    /// <summary>
    /// Builds the key for an encounter type.
    /// Format: {TYPE_KEY_PREFIX}{code}
    /// </summary>
    internal static string BuildTypeKey(string code)
        => $"{TYPE_KEY_PREFIX}{code.ToUpperInvariant()}";

    /// <summary>
    /// Builds the key for a character index entry.
    /// Format: {CHAR_INDEX_PREFIX}{characterId}
    /// </summary>
    internal static string BuildCharacterIndexKey(Guid characterId)
        => $"{CHAR_INDEX_PREFIX}{characterId}";

    /// <summary>
    /// Builds the key for a pair index entry.
    /// Format: {PAIR_INDEX_PREFIX}{pairKey}
    /// </summary>
    internal static string BuildPairIndexKey(string pairKey)
        => $"{PAIR_INDEX_PREFIX}{pairKey}";

    /// <summary>
    /// Builds the key for a location index entry.
    /// Format: {LOCATION_INDEX_PREFIX}{locationId}
    /// </summary>
    internal static string BuildLocationIndexKey(Guid locationId)
        => $"{LOCATION_INDEX_PREFIX}{locationId}";

    /// <summary>
    /// Builds the key for a type-encounter index entry.
    /// Format: {TYPE_ENCOUNTER_INDEX_PREFIX}{typeCode}
    /// </summary>
    internal static string BuildTypeEncounterIndexKey(string typeCode)
        => $"{TYPE_ENCOUNTER_INDEX_PREFIX}{typeCode}";

    /// <summary>
    /// Builds the key for an encounter-perspective index entry.
    /// Format: {ENCOUNTER_PERSPECTIVE_INDEX_PREFIX}{encounterId}
    /// </summary>
    internal static string BuildEncounterPerspectiveIndexKey(Guid encounterId)
        => $"{ENCOUNTER_PERSPECTIVE_INDEX_PREFIX}{encounterId}";

    private async Task<List<EncounterPerspectiveModel>> GetEncounterPerspectivesAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetEncounterPerspectivesAsync");
        // Try new index first for O(1) lookup
        var perspectiveIds = await GetEncounterPerspectiveIdsAsync(encounterId, cancellationToken);

        if (perspectiveIds.Count > 0)
        {
            // Bulk load with parallel decay
            var perspectives = await BulkLoadPerspectivesWithDecayAsync(perspectiveIds, cancellationToken);
            return perspectives.Select(MapToPerspectiveModel).ToList();
        }

        // Fallback for pre-existing encounters without index (legacy data)
        var perspectiveStore = _perspectiveStore;
        var encounterStore = _encounterStore;
        var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
        if (encounter == null) return new List<EncounterPerspectiveModel>();

        var legacyPerspectives = new List<EncounterPerspectiveModel>();
        foreach (var participantId in encounter.ParticipantIds)
        {
            var perspective = await FindPerspectiveByEncounterAndCharacterAsync(encounterId, participantId, cancellationToken);
            if (perspective != null)
            {
                // Apply lazy decay
                perspective = await ApplyLazyDecayAsync(perspectiveStore, perspective, cancellationToken);
                legacyPerspectives.Add(MapToPerspectiveModel(perspective));
            }
        }
        return legacyPerspectives;
    }

    private async Task<PerspectiveData?> FindPerspectiveAsync(Guid encounterId, Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.FindPerspectiveAsync");
        return await FindPerspectiveByEncounterAndCharacterAsync(encounterId, characterId, cancellationToken);
    }

    private async Task<PerspectiveData?> FindPerspectiveByEncounterAndCharacterAsync(Guid encounterId, Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.FindPerspectiveByEncounterAndCharacterAsync");
        // Get character's perspective IDs and find the one for this encounter
        var perspectiveIds = await GetCharacterPerspectiveIdsAsync(characterId, cancellationToken);
        var perspectiveStore = _perspectiveStore;

        foreach (var perspectiveId in perspectiveIds)
        {
            var perspective = await perspectiveStore.GetAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
            if (perspective != null && perspective.EncounterId == encounterId)
            {
                return perspective;
            }
        }
        return null;
    }

    private async Task<int> DeleteEncounterPerspectivesAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.DeleteEncounterPerspectivesAsync");
        var perspectiveStore = _perspectiveStore;

        // Try the new index first for O(1) lookup
        var perspectiveIds = await GetEncounterPerspectiveIdsAsync(encounterId, cancellationToken);

        if (perspectiveIds.Count > 0)
        {
            // Bulk load perspectives using the index
            var perspectiveKeys = perspectiveIds.Select(id => $"{PERSPECTIVE_KEY_PREFIX}{id}").ToList();
            var perspectives = await perspectiveStore.GetBulkAsync(perspectiveKeys, cancellationToken);

            foreach (var (key, perspective) in perspectives)
            {
                if (perspective != null)
                {
                    await perspectiveStore.DeleteAsync(key, cancellationToken);
                    await RemoveFromCharacterIndexAsync(perspective.CharacterId, perspective.PerspectiveId, cancellationToken);
                }
            }

            // Delete the encounter-perspective index itself
            await DeleteEncounterPerspectiveIndexAsync(encounterId, cancellationToken);

            return perspectiveIds.Count;
        }

        // Fallback for pre-existing encounters without index (legacy data)
        var encounterStore = _encounterStore;
        var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
        if (encounter == null) return 0;

        var deleted = 0;
        foreach (var participantId in encounter.ParticipantIds)
        {
            var perspective = await FindPerspectiveByEncounterAndCharacterAsync(encounterId, participantId, cancellationToken);
            if (perspective != null)
            {
                await perspectiveStore.DeleteAsync($"{PERSPECTIVE_KEY_PREFIX}{perspective.PerspectiveId}", cancellationToken);
                await RemoveFromCharacterIndexAsync(participantId, perspective.PerspectiveId, cancellationToken);
                deleted++;
            }
        }
        return deleted;
    }

    private async Task<PerspectiveData> ApplyLazyDecayAsync(IStateStore<PerspectiveData> store, PerspectiveData perspective, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.ApplyLazyDecayAsync");
        if (!_configuration.MemoryDecayEnabled || _configuration.MemoryDecayMode != MemoryDecayMode.Lazy)
            return perspective;

        // Determine decay amount based on time source
        double decayAmount;
        if (_configuration.DecayTimeSource == TimeSource.GameTime)
        {
            // Load encounter for realmId
            var encounter = await _encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{perspective.EncounterId}", cancellationToken);
            if (encounter is null)
                return perspective; // Orphaned perspective — skip

            try
            {
                var fromTime = perspective.LastDecayedAtUnix.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(perspective.LastDecayedAtUnix.Value)
                    : DateTimeOffset.FromUnixTimeSeconds(perspective.CreatedAtUnix);
                var elapsedResponse = await _worldstateClient.GetElapsedGameTimeAsync(
                    new GetElapsedGameTimeRequest
                    {
                        RealmId = encounter.RealmId,
                        FromRealTime = fromTime,
                        ToRealTime = DateTimeOffset.UtcNow
                    }, cancellationToken);

                var gameHours = elapsedResponse.TotalGameSeconds / 3600.0;
                var intervals = gameHours / _configuration.MemoryDecayIntervalHours;
                if (intervals < 1)
                    return perspective;

                decayAmount = intervals * _configuration.MemoryDecayRate;
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Worldstate unavailable for realm {RealmId} during lazy decay, skipping perspective {PerspectiveId}",
                    encounter.RealmId, perspective.PerspectiveId);
                await _messageBus.TryPublishErrorAsync(
                    "character-encounter",
                    "ApplyLazyDecay",
                    "ApiException",
                    ex.Message,
                    dependency: "worldstate",
                    endpoint: "worldstate/get-elapsed-game-time",
                    stack: ex.StackTrace,
                    cancellationToken: cancellationToken);
                return perspective;
            }
        }
        else
        {
            var (needsDecay, _) = CalculateRealTimeDecay(perspective);
            if (!needsDecay) return perspective;
            decayAmount = GetRealTimeDecayAmount(perspective);
        }

        // Re-fetch with ETag for optimistic concurrency (prevents double-decay from concurrent reads)
        var perspectiveKey = $"{PERSPECTIVE_KEY_PREFIX}{perspective.PerspectiveId}";
        var (freshPerspective, etag) = await store.GetWithETagAsync(perspectiveKey, cancellationToken);
        if (freshPerspective == null) return perspective;

        // Recalculate on fresh data in case another instance already decayed
        if (_configuration.DecayTimeSource == TimeSource.RealTime)
        {
            var (stillNeedsDecay, _) = CalculateRealTimeDecay(freshPerspective);
            if (!stillNeedsDecay) return freshPerspective;
            decayAmount = GetRealTimeDecayAmount(freshPerspective);
        }

        var previousStrength = freshPerspective.MemoryStrength;
        freshPerspective.MemoryStrength = Math.Max(0, freshPerspective.MemoryStrength - (float)decayAmount);
        freshPerspective.LastDecayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // GetWithETagAsync returns non-null etag for existing records;
        // coalesce satisfies compiler's nullable analysis (will never execute)
        var saveResult = await store.TrySaveAsync(perspectiveKey, freshPerspective, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (saveResult == null)
        {
            // Concurrent modification - another instance likely already applied decay
            _logger.LogDebug("Concurrent modification during lazy decay for perspective {PerspectiveId}, skipping", perspective.PerspectiveId);
            return freshPerspective;
        }

        // Check if faded below threshold
        if (previousStrength >= _configuration.MemoryFadeThreshold && freshPerspective.MemoryStrength < _configuration.MemoryFadeThreshold)
        {
            await _messageBus.PublishEncounterMemoryFadedAsync(new EncounterMemoryFadedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                EncounterId = freshPerspective.EncounterId,
                CharacterId = freshPerspective.CharacterId,
                PerspectiveId = freshPerspective.PerspectiveId,
                PreviousStrength = previousStrength,
                NewStrength = freshPerspective.MemoryStrength,
                FadeThreshold = (float)_configuration.MemoryFadeThreshold
            }, cancellationToken);
        }

        return freshPerspective;
    }

    /// <summary>
    /// Calculates whether a perspective needs real-time decay and if it will fade below threshold.
    /// Used when DecayTimeSource is RealTime.
    /// </summary>
    private (bool needsDecay, bool willFade) CalculateRealTimeDecay(PerspectiveData perspective)
    {
        if (perspective.MemoryStrength <= 0) return (false, false);

        var lastDecayed = perspective.LastDecayedAtUnix.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(perspective.LastDecayedAtUnix.Value)
            : DateTimeOffset.FromUnixTimeSeconds(perspective.CreatedAtUnix);

        var hoursSinceLastDecay = (DateTimeOffset.UtcNow - lastDecayed).TotalHours;
        var intervalsElapsed = hoursSinceLastDecay / _configuration.MemoryDecayIntervalHours;

        if (intervalsElapsed < 1) return (false, false);

        var decayAmt = GetRealTimeDecayAmount(perspective);
        var newStrength = perspective.MemoryStrength - decayAmt;
        var willFade = perspective.MemoryStrength >= _configuration.MemoryFadeThreshold &&
                        newStrength < _configuration.MemoryFadeThreshold;

        return (true, willFade);
    }

    /// <summary>
    /// Calculates real-time decay amount for a perspective based on wall-clock time elapsed.
    /// Used when DecayTimeSource is RealTime.
    /// </summary>
    private float GetRealTimeDecayAmount(PerspectiveData perspective)
    {
        var lastDecayed = perspective.LastDecayedAtUnix.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(perspective.LastDecayedAtUnix.Value)
            : DateTimeOffset.FromUnixTimeSeconds(perspective.CreatedAtUnix);

        var hoursSinceLastDecay = (DateTimeOffset.UtcNow - lastDecayed).TotalHours;
        var intervalsElapsed = (int)(hoursSinceLastDecay / _configuration.MemoryDecayIntervalHours);

        return (float)(intervalsElapsed * _configuration.MemoryDecayRate);
    }

    private static EmotionalImpact GetDefaultEmotionalImpactForOutcome(EncounterOutcome outcome)
    {
        return outcome switch
        {
            EncounterOutcome.Positive => EmotionalImpact.Gratitude,
            EncounterOutcome.Negative => EmotionalImpact.Anger,
            EncounterOutcome.Neutral => EmotionalImpact.Indifference,
            EncounterOutcome.Memorable => EmotionalImpact.Respect,
            EncounterOutcome.Transformative => EmotionalImpact.Pride,
            _ => EmotionalImpact.Indifference
        };
    }

    private float? GetDefaultSentimentShiftForOutcome(EncounterOutcome outcome)
    {
        return outcome switch
        {
            EncounterOutcome.Positive => (float)_configuration.SentimentShiftPositive,
            EncounterOutcome.Negative => (float)_configuration.SentimentShiftNegative,
            EncounterOutcome.Neutral => 0f,
            EncounterOutcome.Memorable => (float)_configuration.SentimentShiftMemorable,
            EncounterOutcome.Transformative => (float)_configuration.SentimentShiftTransformative,
            _ => 0f
        };
    }

    // ============================================================================
    // Mapping Helpers
    // ============================================================================

    private static EncounterTypeResponse MapToEncounterTypeResponse(EncounterTypeData data)
    {
        return new EncounterTypeResponse
        {
            TypeId = data.TypeId,
            Code = data.Code,
            Name = data.Name,
            Description = data.Description,
            IsBuiltIn = data.IsBuiltIn,
            DefaultEmotionalImpact = data.DefaultEmotionalImpact,
            SortOrder = data.SortOrder,
            IsActive = data.IsActive,
            IsDeprecated = data.IsDeprecated,
            DeprecatedAt = data.DeprecatedAt,
            DeprecationReason = data.DeprecationReason,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix)
        };
    }

    private static EncounterModel MapToEncounterModel(EncounterData data)
    {
        return new EncounterModel
        {
            EncounterId = data.EncounterId,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(data.Timestamp),
            RealmId = data.RealmId,
            LocationId = data.LocationId,
            EncounterTypeCode = data.EncounterTypeCode,
            Context = data.Context,
            Outcome = data.Outcome,
            ParticipantIds = data.ParticipantIds,
            Metadata = data.Metadata,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix)
        };
    }

    private static EncounterPerspectiveModel MapToPerspectiveModel(PerspectiveData data)
    {
        return new EncounterPerspectiveModel
        {
            PerspectiveId = data.PerspectiveId,
            EncounterId = data.EncounterId,
            CharacterId = data.CharacterId,
            EmotionalImpact = data.EmotionalImpact,
            ImpactIntensity = data.ImpactIntensity,
            SentimentShift = data.SentimentShift,
            MemoryStrength = data.MemoryStrength,
            RememberedAs = data.RememberedAs,
            LastDecayedAt = data.LastDecayedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(data.LastDecayedAtUnix.Value) : null,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix),
            UpdatedAt = data.UpdatedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(data.UpdatedAtUnix.Value) : null
        };
    }
}
