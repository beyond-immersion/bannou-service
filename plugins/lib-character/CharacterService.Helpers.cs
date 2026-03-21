using BeyondImmersion.Bannou.Character.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Character;

// =============================================================================
// CharacterService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by CharacterService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (CharacterService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ICharacterService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (CharacterService.Helpers.cs):
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
/// Private and internal helper methods for CharacterService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class CharacterService
{
    #region Enrichment/Compression Helper Methods

    /// <summary>
    /// Builds family tree from relationships.
    /// Uses parallel lookups for relationship types and bulk loading for related characters.
    /// </summary>
    private async Task<FamilyTreeResponse?> BuildFamilyTreeAsync(Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.BuildFamilyTreeAsync");
        try
        {
            var result = await _relationshipClient.ListRelationshipsByEntityAsync(
                new ListRelationshipsByEntityRequest
                {
                    EntityId = characterId,
                    EntityType = EntityType.Character
                },
                cancellationToken);

            if (result == null || result.Relationships.Count == 0)
            {
                return new FamilyTreeResponse();
            }

            // Build type code lookup from relationship type IDs - PARALLEL
            var uniqueTypeIds = result.Relationships
                .Select(r => r.RelationshipTypeId)
                .Distinct()
                .ToList();

            var typeCodeLookup = await BuildTypeCodeLookupAsync(uniqueTypeIds, cancellationToken);

            // Collect all related character IDs for bulk loading
            var relatedCharacterIds = result.Relationships
                .Select(r => r.Entity1Id == characterId ? r.Entity2Id : r.Entity1Id)
                .Distinct()
                .ToList();

            // Bulk load all related characters in one call
            var characterLookup = await BulkLoadCharactersAsync(relatedCharacterIds, cancellationToken);

            var familyTree = new FamilyTreeResponse
            {
                Parents = new List<FamilyMember>(),
                Children = new List<FamilyMember>(),
                Siblings = new List<FamilyMember>(),
                Spouses = new List<FamilyMember>(),
                PastLives = new List<PastLifeReference>()
            };

            foreach (var rel in result.Relationships)
            {
                var isEntity1 = rel.Entity1Id == characterId;
                var relatedId = isEntity1 ? rel.Entity2Id : rel.Entity1Id;

                // Look up type code
                if (!typeCodeLookup.TryGetValue(rel.RelationshipTypeId, out var typeCode))
                {
                    continue; // Skip relationships with unknown types
                }

                // Get related character info from pre-loaded lookup
                characterLookup.TryGetValue(relatedId, out var relatedCharacter);
                var name = relatedCharacter?.Name;
                var isAlive = relatedCharacter?.Status == CharacterStatus.Alive;

                // Parent relationship
                if (typeCode == "PARENT" || typeCode == "MOTHER" || typeCode == "FATHER" || typeCode == "STEP_PARENT")
                {
                    if (isEntity1)
                    {
                        // Entity1 is PARENT of Entity2, so this character IS the parent
                        familyTree.Children.Add(new FamilyMember
                        {
                            CharacterId = relatedId,
                            Name = name,
                            RelationshipType = typeCode,
                            IsAlive = isAlive
                        });
                    }
                    else
                    {
                        // Entity2 is the child, this character is the child, rel points to parent
                        familyTree.Parents.Add(new FamilyMember
                        {
                            CharacterId = relatedId,
                            Name = name,
                            RelationshipType = typeCode,
                            IsAlive = isAlive
                        });
                    }
                }
                // Child relationship
                else if (typeCode == "CHILD" || typeCode == "SON" || typeCode == "DAUGHTER" || typeCode == "STEP_CHILD")
                {
                    if (isEntity1)
                    {
                        // Entity1 is CHILD of Entity2
                        familyTree.Parents.Add(new FamilyMember
                        {
                            CharacterId = relatedId,
                            Name = name,
                            RelationshipType = typeCode,
                            IsAlive = isAlive
                        });
                    }
                    else
                    {
                        familyTree.Children.Add(new FamilyMember
                        {
                            CharacterId = relatedId,
                            Name = name,
                            RelationshipType = typeCode,
                            IsAlive = isAlive
                        });
                    }
                }
                // Sibling relationship
                else if (typeCode == "SIBLING" || typeCode == "BROTHER" || typeCode == "SISTER" || typeCode == "HALF_SIBLING")
                {
                    familyTree.Siblings.Add(new FamilyMember
                    {
                        CharacterId = relatedId,
                        Name = name,
                        RelationshipType = typeCode,
                        IsAlive = isAlive
                    });
                }
                // Spouse relationship
                else if (typeCode == "SPOUSE" || typeCode == "HUSBAND" || typeCode == "WIFE")
                {
                    familyTree.Spouses.Add(new FamilyMember
                    {
                        CharacterId = relatedId,
                        Name = name,
                        RelationshipType = typeCode,
                        IsAlive = isAlive
                    });
                }
                // Past life (reincarnation)
                else if (typeCode == "INCARNATION")
                {
                    // This character IS the INCARNATION of another (reincarnated FROM)
                    if (!isEntity1)
                    {
                        familyTree.PastLives.Add(new PastLifeReference
                        {
                            CharacterId = relatedId,
                            Name = name,
                            DeathDate = relatedCharacter?.DeathDate
                        });
                    }
                }
            }

            return familyTree;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return new FamilyTreeResponse();
        }
    }

    /// <summary>
    /// Builds relationship type code lookup using parallel API calls.
    /// </summary>
    private async Task<Dictionary<Guid, string>> BuildTypeCodeLookupAsync(
        List<Guid> typeIds,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.BuildTypeCodeLookupAsync");
        var typeCodeLookup = new Dictionary<Guid, string>();

        if (typeIds.Count == 0)
            return typeCodeLookup;

        // Launch all lookups in parallel
        var lookupTasks = typeIds.Select(async typeId =>
        {
            try
            {
                var typeResponse = await _relationshipClient.GetRelationshipTypeAsync(
                    new GetRelationshipTypeRequest { RelationshipTypeId = typeId },
                    cancellationToken);
                return (typeId, code: typeResponse?.Code);
            }
            catch (ApiException)
            {
                _logger.LogWarning("Could not look up relationship type {TypeId}", typeId);
                return (typeId, code: (string?)null);
            }
        }).ToList();

        var results = await Task.WhenAll(lookupTasks);

        foreach (var (typeId, code) in results)
        {
            if (code != null)
            {
                typeCodeLookup[typeId] = code;
            }
        }

        return typeCodeLookup;
    }

    /// <summary>
    /// Bulk loads characters by ID using the global index for realm resolution.
    /// Returns a dictionary for O(1) lookup during family tree construction.
    /// </summary>
    private async Task<Dictionary<Guid, CharacterModel>> BulkLoadCharactersAsync(
        List<Guid> characterIds,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.BulkLoadCharactersAsync");
        var result = new Dictionary<Guid, CharacterModel>();

        if (characterIds.Count == 0)
            return result;

        // Step 1: Bulk load global index entries to get realm IDs
        var globalIndexKeys = characterIds
            .Select(id => $"character-global-index:{id}")
            .ToList();

        var globalIndexResults = await _globalIndexStore.GetBulkAsync(globalIndexKeys, cancellationToken);

        // Step 2: Build character keys from realm mappings
        var characterKeys = new List<string>();
        var keyToIdMap = new Dictionary<string, Guid>();

        foreach (var (globalIndexKey, realmId) in globalIndexResults)
        {
            if (string.IsNullOrEmpty(realmId))
                continue;

            // Extract character ID from global index key (format: "character-global-index:{id}")
            var characterIdStr = globalIndexKey.Replace("character-global-index:", "");
            if (Guid.TryParse(characterIdStr, out var characterId))
            {
                var characterKey = BuildCharacterKey(realmId, characterIdStr);
                characterKeys.Add(characterKey);
                keyToIdMap[characterKey] = characterId;
            }
        }

        if (characterKeys.Count == 0)
            return result;

        // Step 3: Bulk load all characters
        var characterResults = await _characterStore.GetBulkAsync(characterKeys, cancellationToken);

        foreach (var (key, character) in characterResults)
        {
            if (character != null && keyToIdMap.TryGetValue(key, out var characterId))
            {
                result[characterId] = character;
            }
        }

        return result;
    }

    /// <summary>
    /// Generates a text summary of family relationships.
    /// </summary>
    private async Task<string?> GenerateFamilySummaryAsync(Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.GenerateFamilySummaryAsync");
        var familyTree = await BuildFamilyTreeAsync(characterId, cancellationToken);
        if (familyTree == null)
            return null;

        var parts = new List<string>();

        if (familyTree.Spouses?.Count > 0)
        {
            var spouseNames = familyTree.Spouses.Select(s => s.Name ?? "unknown");
            parts.Add($"married to {string.Join(" and ", spouseNames)}");
        }

        var childCount = familyTree.Children?.Count ?? 0;
        if (childCount > 0)
            parts.Add($"parent of {childCount}");

        var parentCount = familyTree.Parents?.Count ?? 0;
        if (parentCount == 0)
            parts.Add("orphaned");
        else if (parentCount == 1)
            parts.Add("single parent household");

        var pastLivesCount = familyTree.PastLives?.Count ?? 0;
        if (pastLivesCount > 0)
            parts.Add($"reincarnated from {pastLivesCount} past life(s)");

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static CharacterArchiveModel MapToArchiveModel(CharacterArchive archive)
    {
        return new CharacterArchiveModel
        {
            CharacterId = archive.CharacterId,
            Name = archive.Name,
            RealmId = archive.RealmId,
            SpeciesId = archive.SpeciesId,
            BirthDateUnix = archive.BirthDate.ToUnixTimeSeconds(),
            DeathDateUnix = archive.DeathDate.ToUnixTimeSeconds(),
            CompressedAtUnix = archive.CompressedAt.ToUnixTimeSeconds(),
            PersonalitySummary = archive.PersonalitySummary,
            KeyBackstoryPoints = archive.KeyBackstoryPoints?.ToList(),
            MajorLifeEvents = archive.MajorLifeEvents?.ToList(),
            FamilySummary = archive.FamilySummary
        };
    }

    private static CharacterArchive MapFromArchiveModel(CharacterArchiveModel model)
    {
        return new CharacterArchive
        {
            CharacterId = model.CharacterId,
            Name = model.Name,
            RealmId = model.RealmId,
            SpeciesId = model.SpeciesId,
            BirthDate = DateTimeOffset.FromUnixTimeSeconds(model.BirthDateUnix),
            DeathDate = DateTimeOffset.FromUnixTimeSeconds(model.DeathDateUnix),
            CompressedAt = DateTimeOffset.FromUnixTimeSeconds(model.CompressedAtUnix),
            PersonalitySummary = model.PersonalitySummary,
            KeyBackstoryPoints = model.KeyBackstoryPoints,
            MajorLifeEvents = model.MajorLifeEvents,
            FamilySummary = model.FamilySummary
        };
    }

    #endregion

    #region Helper Methods

    internal static string BuildCharacterKey(string realmId, string characterId)
        => $"{CHARACTER_KEY_PREFIX}{realmId}:{characterId}";

    internal static string BuildRealmIndexKey(string realmId)
        => $"{REALM_INDEX_KEY_PREFIX}{realmId}";

    #region Validation Helpers

    /// <summary>
    /// Validates that a realm exists and is active (not deprecated).
    /// </summary>
    private async Task<(bool exists, bool isActive)> ValidateRealmAsync(Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.ValidateRealmAsync");
        try
        {
            var response = await _realmClient.RealmExistsAsync(
                new RealmExistsRequest { RealmId = realmId },
                cancellationToken);
            return (response.Exists, response.IsActive);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return (false, false);
        }
        // Let ApiException propagate naturally so callers classify it as ServiceUnavailable (IMPLEMENTATION TENETS)
        catch (Exception ex) when (ex is not ApiException)
        {
            _logger.LogError(ex, "Could not validate realm {RealmId} - failing operation (fail closed)", realmId);
            throw;
        }
    }

    /// <summary>
    /// Validates that a species exists and is available in the specified realm.
    /// </summary>
    private async Task<(bool exists, bool isInRealm)> ValidateSpeciesAsync(Guid speciesId, Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.ValidateSpeciesAsync");
        try
        {
            var speciesResponse = await _speciesClient.GetSpeciesAsync(
                new GetSpeciesRequest { SpeciesId = speciesId },
                cancellationToken);

            if (speciesResponse == null)
            {
                return (false, false);
            }

            // Check if species is available in the specified realm
            var isInRealm = speciesResponse.RealmIds?.Contains(realmId) ?? false;
            return (true, isInRealm);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return (false, false);
        }
        // Let ApiException propagate naturally so callers classify it as ServiceUnavailable (IMPLEMENTATION TENETS)
        catch (Exception ex) when (ex is not ApiException)
        {
            _logger.LogError(ex, "Could not validate species {SpeciesId} - failing operation (fail closed)", speciesId);
            throw;
        }
    }

    #endregion

    private async Task<CharacterModel?> FindCharacterByIdAsync(string characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.FindCharacterByIdAsync");
        // Use global character index to find realm for character ID lookup
        // Global index is maintained by AddCharacterToRealmIndexAsync/RemoveCharacterFromRealmIndexAsync
        var globalIndexKey = $"character-global-index:{characterId}";
        var realmId = await _globalIndexStore.GetAsync(globalIndexKey, cancellationToken);

        if (string.IsNullOrEmpty(realmId))
        {
            _logger.LogDebug("Character {CharacterId} not found in global index", characterId);
            return null;
        }

        var characterKey = BuildCharacterKey(realmId, characterId);
        return await _characterStore.GetAsync(characterKey, cancellationToken);
    }

    private async Task<(StatusCodes, CharacterListResponse?)> GetCharactersByRealmInternalAsync(
        string realmId,
        CharacterStatus? statusFilter,
        Guid? speciesFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.GetCharactersByRealmInternalAsync");
        var offset = (page - 1) * pageSize;

        var conditions = BuildCharacterQueryConditions(realmId, statusFilter, speciesFilter);

        var sortSpec = new JsonSortSpec
        {
            Path = "$.Name",
            Descending = false
        };

        var result = await _characterJsonStore.JsonQueryPagedAsync(
            conditions,
            offset,
            pageSize,
            sortSpec,
            cancellationToken);

        var pagedCharacters = result.Items
            .Select(item => MapToCharacterResponse(item.Value))
            .ToList();

        var response = new CharacterListResponse
        {
            Characters = pagedCharacters,
            TotalCount = (int)result.TotalCount,
            Page = page,
            PageSize = pageSize,
            HasNextPage = result.HasMore,
            HasPreviousPage = page > 1
        };

        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Builds MySQL JSON query conditions for character listing.
    /// Uses server-side filtering to avoid loading all characters into memory.
    /// </summary>
    private static List<QueryCondition> BuildCharacterQueryConditions(
        string realmId,
        CharacterStatus? statusFilter,
        Guid? speciesFilter)
    {
        var conditions = new List<QueryCondition>
        {
            // Type discriminator: only CharacterModel records have CharacterId
            new QueryCondition { Path = "$.CharacterId", Operator = QueryOperator.Exists, Value = true },
            // Realm filter: server-side partition by realm
            new QueryCondition { Path = "$.RealmId", Operator = QueryOperator.Equals, Value = realmId }
        };

        if (statusFilter.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.Status",
                Operator = QueryOperator.Equals,
                Value = statusFilter.Value.ToString()
            });
        }

        if (speciesFilter.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.SpeciesId",
                Operator = QueryOperator.Equals,
                Value = speciesFilter.Value.ToString()
            });
        }

        return conditions;
    }

    private async Task AddCharacterToRealmIndexAsync(string realmId, string characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.AddCharacterToRealmIndexAsync");
        var realmIndexKey = BuildRealmIndexKey(realmId);

        // Retry loop for optimistic concurrency
        var maxRetries = _configuration.RealmIndexUpdateMaxRetries;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            var (characterIds, etag) = await _realmIndexStore.GetWithETagAsync(realmIndexKey, cancellationToken);
            characterIds ??= new List<string>();

            if (!characterIds.Contains(characterId))
            {
                characterIds.Add(characterId);

                // If no prior value (null etag), just save directly
                if (etag == null)
                {
                    await _realmIndexStore.SaveAsync(realmIndexKey, characterIds, cancellationToken: cancellationToken);
                    break;
                }

                // Otherwise use optimistic concurrency
                if (await _realmIndexStore.TrySaveAsync(realmIndexKey, characterIds, etag, cancellationToken: cancellationToken) != null)
                    break;

                // Retry on conflict
                if (retry < maxRetries - 1)
                {
                    _logger.LogDebug("Realm index update conflict, retrying ({Retry}/{MaxRetries})", retry + 1, maxRetries);
                    continue;
                }
                throw new InvalidOperationException($"Failed to update realm index after {maxRetries} retries");
            }
            else
            {
                // Already in the list, no update needed
                break;
            }
        }

        // Also add to global index for ID-based lookups
        var globalIndexKey = $"character-global-index:{characterId}";
        await _globalIndexStore.SaveAsync(globalIndexKey, realmId, cancellationToken: cancellationToken);
    }

    private async Task RemoveCharacterFromRealmIndexAsync(string realmId, string characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.RemoveCharacterFromRealmIndexAsync");
        var realmIndexKey = BuildRealmIndexKey(realmId);

        // Retry loop for optimistic concurrency
        var maxRetries = _configuration.RealmIndexUpdateMaxRetries;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            var (characterIds, etag) = await _realmIndexStore.GetWithETagAsync(realmIndexKey, cancellationToken);

            // If list doesn't exist, nothing to remove
            if (characterIds == null || etag == null)
                break;

            if (characterIds.Remove(characterId))
            {
                if (await _realmIndexStore.TrySaveAsync(realmIndexKey, characterIds, etag, cancellationToken: cancellationToken) != null)
                    break;

                // Retry on conflict
                if (retry < maxRetries - 1)
                {
                    _logger.LogDebug("Realm index update conflict, retrying ({Retry}/{MaxRetries})", retry + 1, maxRetries);
                    continue;
                }
                throw new InvalidOperationException($"Failed to update realm index after {maxRetries} retries");
            }
            else
            {
                // Not in the list, no update needed
                break;
            }
        }

        // Remove from global index
        var globalIndexKey = $"character-global-index:{characterId}";
        await _globalIndexStore.DeleteAsync(globalIndexKey, cancellationToken);
    }

    private static CharacterResponse MapToCharacterResponse(CharacterModel model)
    {
        return new CharacterResponse
        {
            CharacterId = model.CharacterId,
            Name = model.Name,
            RealmId = model.RealmId,
            SpeciesId = model.SpeciesId,
            BirthDate = model.BirthDate,
            DeathDate = model.DeathDate,
            Status = model.Status,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    #endregion

    #region Event Publishing

    /// <summary>
    /// Publishes character created event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
    private async Task PublishCharacterCreatedEventAsync(CharacterModel character)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.PublishCharacterCreatedEventAsync");
        var eventModel = new CharacterCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = character.CharacterId,
            Name = character.Name,
            RealmId = character.RealmId,
            SpeciesId = character.SpeciesId,
            BirthDate = character.BirthDate,
            DeathDate = character.DeathDate,
            Status = character.Status,
            CreatedAt = character.CreatedAt,
            UpdatedAt = character.UpdatedAt
        };

        await _messageBus.PublishCharacterCreatedAsync(eventModel);
        _logger.LogDebug("Published CharacterCreatedEvent for character: {CharacterId}", character.CharacterId);
    }

    /// <summary>
    /// Publishes character updated event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
    private async Task PublishCharacterUpdatedEventAsync(CharacterModel character, IEnumerable<string> changedFields)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.PublishCharacterUpdatedEventAsync");
        var eventModel = new CharacterUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = character.CharacterId,
            Name = character.Name,
            RealmId = character.RealmId,
            SpeciesId = character.SpeciesId,
            BirthDate = character.BirthDate,
            DeathDate = character.DeathDate,
            Status = character.Status,
            CreatedAt = character.CreatedAt,
            UpdatedAt = character.UpdatedAt,
            ChangedFields = changedFields.ToList()
        };

        await _messageBus.PublishCharacterUpdatedAsync(eventModel);
        _logger.LogDebug("Published CharacterUpdatedEvent for character: {CharacterId}", character.CharacterId);
    }

    /// <summary>
    /// Publishes character deleted event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
    private async Task PublishCharacterDeletedEventAsync(CharacterModel character, string? deletedReason = null)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.PublishCharacterDeletedEventAsync");
        var eventModel = new CharacterDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = character.CharacterId,
            Name = character.Name,
            RealmId = character.RealmId,
            SpeciesId = character.SpeciesId,
            BirthDate = character.BirthDate,
            DeathDate = character.DeathDate,
            Status = character.Status,
            CreatedAt = character.CreatedAt,
            UpdatedAt = character.UpdatedAt,
            DeletedReason = deletedReason
        };

        await _messageBus.PublishCharacterDeletedAsync(eventModel);
        _logger.LogDebug("Published CharacterDeletedEvent for character: {CharacterId}", character.CharacterId);
    }

    /// <summary>
    /// Publishes character realm joined event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
    private async Task PublishCharacterRealmJoinedEventAsync(Guid characterId, Guid realmId, Guid? previousRealmId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.PublishCharacterRealmJoinedEventAsync");
        var eventModel = new CharacterRealmJoinedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = characterId,
            RealmId = realmId,
            PreviousRealmId = previousRealmId
        };

        await _messageBus.PublishCharacterRealmJoinedAsync(eventModel);
        _logger.LogDebug("Published CharacterRealmJoinedEvent for character: {CharacterId}", characterId);
    }

    /// <summary>
    /// Publishes character realm left event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
    private async Task PublishCharacterRealmLeftEventAsync(Guid characterId, Guid realmId, CharacterRealmLeftReason reason)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.PublishCharacterRealmLeftEventAsync");
        var eventModel = new CharacterRealmLeftEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = characterId,
            RealmId = realmId,
            Reason = reason
        };

        await _messageBus.PublishCharacterRealmLeftAsync(eventModel);
        _logger.LogDebug("Published CharacterRealmLeftEvent for character: {CharacterId}", characterId);
    }

    /// <summary>
    /// Publishes character updated client event to connected WebSocket sessions via Entity Session Registry.
    /// Includes only the changed field values so the client can re-render without a full model fetch.
    /// </summary>
    private async Task PublishCharacterUpdatedClientEventAsync(
        CharacterModel character, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.PublishCharacterUpdatedClientEventAsync");

        var changedFieldsList = changedFields.ToList();
        var clientEvent = new CharacterUpdatedClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = character.CharacterId,
            ChangedFields = changedFieldsList
        };

        // Conditionally populate optional fields based on what changed
        if (changedFieldsList.Contains("name"))
        {
            clientEvent.Name = character.Name;
        }

        if (changedFieldsList.Contains("status"))
        {
            clientEvent.Status = character.Status;
        }

        if (changedFieldsList.Contains("deathDate"))
        {
            clientEvent.DeathDate = character.DeathDate;
        }

        var sessionsNotified = await _entitySessionRegistry.PublishToEntitySessionsAsync(
            "character", character.CharacterId, clientEvent, cancellationToken);

        _logger.LogDebug("Published CharacterUpdatedClientEvent for character {CharacterId} to {SessionCount} session(s)",
            character.CharacterId, sessionsNotified);
    }

    /// <summary>
    /// Publishes character realm transferred client event to connected WebSocket sessions via Entity Session Registry.
    /// Distinct from the updated client event because realm transfer requires a full UI context switch on the client.
    /// </summary>
    private async Task PublishCharacterRealmTransferredClientEventAsync(
        Guid characterId, Guid previousRealmId, Guid newRealmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character", "CharacterService.PublishCharacterRealmTransferredClientEventAsync");

        var clientEvent = new CharacterRealmTransferredClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = characterId,
            PreviousRealmId = previousRealmId,
            NewRealmId = newRealmId
        };

        var sessionsNotified = await _entitySessionRegistry.PublishToEntitySessionsAsync(
            "character", characterId, clientEvent, cancellationToken);

        _logger.LogDebug("Published CharacterRealmTransferredClientEvent for character {CharacterId} to {SessionCount} session(s)",
            characterId, sessionsNotified);
    }

    #endregion
}
