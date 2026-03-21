using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;

namespace BeyondImmersion.BannouService.Affix;

// =============================================================================
// AffixService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by AffixService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (AffixService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IAffixService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (AffixService.Helpers.cs):
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
/// Private and internal helper methods for AffixService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class AffixService
{
    #region Private Helpers

    /// <summary>Gets definition from cache with read-through to store.</summary>
    private async Task<AffixDefinitionModel?> GetDefinitionWithCacheAsync(Guid definitionId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.GetDefinitionWithCache");

        var cached = await _definitionCache.GetAsync(BuildDefinitionCacheKey(definitionId), ct);
        if (cached != null) return cached;

        var stored = await _definitionStore.GetAsync(BuildDefinitionKey(definitionId), ct);
        if (stored != null)
            await _definitionCache.SaveAsync(BuildDefinitionCacheKey(definitionId), stored, new StateOptions { Ttl = _configuration.DefinitionCacheTtlSeconds }, ct);

        return stored;
    }

    /// <summary>Gets instance from cache with read-through to store.</summary>
    private async Task<AffixInstanceModel?> GetInstanceWithCacheAsync(Guid itemInstanceId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.GetInstanceWithCache");

        var cached = await _instanceCache.GetAsync(BuildInstanceCacheKey(itemInstanceId), ct);
        if (cached != null) return cached;

        var stored = await _instanceStore.GetAsync(BuildInstanceKey(itemInstanceId), ct);
        if (stored != null)
            await _instanceCache.SaveAsync(BuildInstanceCacheKey(itemInstanceId), stored, new StateOptions { Ttl = _configuration.InstanceCacheTtlSeconds }, ct);

        return stored;
    }

    /// <summary>Rolls values for a definition's stat grants.</summary>
    private static double[] RollValuesForDefinition(AffixDefinitionModel definition, ImplicitDefinitionRef? overrides = null, double? percentileTarget = null)
    {
        var random = Random.Shared;
        var values = new double[definition.StatGrants.Length];
        for (var i = 0; i < definition.StatGrants.Length; i++)
        {
            var grant = definition.StatGrants[i];
            var min = overrides?.MinValueOverride ?? grant.MinValue;
            var max = overrides?.MaxValueOverride ?? grant.MaxValue;

            if (percentileTarget.HasValue)
            {
                // Bias toward percentile target
                var target = min + (max - min) * percentileTarget.Value;
                var spread = (max - min) * 0.1;
                values[i] = Math.Clamp(target + (random.NextDouble() - 0.5) * spread, min, max);
            }
            else
            {
                values[i] = min + random.NextDouble() * (max - min);
            }
            values[i] = Math.Round(values[i], 2);
        }
        return values;
    }

    /// <summary>Builds a pool of eligible definitions for generation.</summary>
    private async Task<CachedAffixPool> BuildPoolAsync(Guid gameServiceId, string itemClass, string slotType, int ilvlBucket, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.BuildPool");

        var upperBound = ilvlBucket + _configuration.ItemLevelBucketSize;
        var definitions = await _definitionQueryStore.QueryAsync(
            d => d.GameServiceId == gameServiceId && d.SlotType == slotType && !d.IsDeprecated && d.RequiredItemLevel <= upperBound, ct);

        var pool = new CachedAffixPool();
        foreach (var def in definitions)
        {
            if (def.ValidItemClasses != null && !def.ValidItemClasses.Contains(itemClass))
                continue;

            pool.Entries.Add(new CachedPoolEntry
            {
                DefinitionId = def.DefinitionId,
                DefinitionCode = def.Code,
                ModGroup = def.ModGroup,
                Tier = def.Tier,
                BaseWeight = def.SpawnWeight,
                StatGrants = def.StatGrants,
                RequiredItemLevel = def.RequiredItemLevel,
                RequiredInfluences = def.RequiredInfluences,
                SpawnTagModifiers = def.SpawnTagModifiers
            });
            pool.TotalWeight += def.SpawnWeight;
        }

        return pool;
    }

    /// <summary>Selects a weighted random definition from the pool.</summary>
    private async Task<AffixDefinitionModel?> SelectWeightedAffixAsync(
        Guid gameServiceId, string itemClass, string slotType, int itemLevel,
        HashSet<string> usedModGroups, ICollection<WeightModifier>? weightModifiers,
        ICollection<string>? influences, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.SelectWeightedAffix");

        var (_, poolResponse) = await GenerateAffixPoolAsync(new GenerateAffixPoolRequest
        {
            GameServiceId = gameServiceId,
            ItemClass = itemClass,
            ItemLevel = itemLevel,
            SlotType = slotType,
            ExistingModGroups = usedModGroups.ToList(),
            WeightModifiers = weightModifiers?.ToList(),
            Influences = influences?.ToList()
        }, ct);

        if (poolResponse == null || poolResponse.Entries.Count == 0 || poolResponse.TotalWeight <= 0)
            return null;

        var roll = Random.Shared.Next(poolResponse.TotalWeight);
        var cumulative = 0;
        foreach (var entry in poolResponse.Entries)
        {
            cumulative += entry.EffectiveWeight;
            if (roll < cumulative)
            {
                return await GetDefinitionWithCacheAsync(entry.DefinitionId, ct);
            }
        }

        return null;
    }

    /// <summary>Computes effective rarity from slot counts.</summary>
    private static string ComputeEffectiveRarity(AffixInstanceModel instance)
        => DetermineRarity(instance.PrefixSlots.Count, instance.SuffixSlots.Count);

    /// <summary>Determines rarity from prefix/suffix counts.</summary>
    private static string DetermineRarity(int prefixCount, int suffixCount)
    {
        var total = prefixCount + suffixCount;
        return total switch
        {
            0 => "normal",
            <= 2 => "magic",
            _ => "rare"
        };
    }

    /// <summary>Gets the slot list for a given slot type string.</summary>
    private static List<AffixSlotModel> GetSlotListForType(AffixInstanceModel instance, string slotType)
    {
        return slotType.ToLowerInvariant() switch
        {
            "prefix" => instance.PrefixSlots,
            "suffix" => instance.SuffixSlots,
            "enchant" => instance.EnchantSlots,
            "implicit" => instance.ImplicitSlots,
            _ => instance.PrefixSlots
        };
    }

    /// <summary>Gets max slot count for a slot type.</summary>
    private int GetMaxSlotsForType(string slotType)
    {
        return slotType.ToLowerInvariant() switch
        {
            "prefix" => _configuration.DefaultMaxPrefixes,
            "suffix" => _configuration.DefaultMaxSuffixes,
            "enchant" => _configuration.MaxAffixesPerItem,
            "implicit" => _configuration.MaxAffixesPerItem,
            _ => _configuration.DefaultMaxPrefixes
        };
    }

    /// <summary>Finds an affix slot by definition ID across all slot types.</summary>
    private static (AffixSlotModel? slot, List<AffixSlotModel>? list) FindSlotByDefinitionId(AffixInstanceModel instance, Guid definitionId)
    {
        foreach (var slot in instance.ImplicitSlots)
            if (slot.DefinitionId == definitionId) return (slot, instance.ImplicitSlots);
        foreach (var slot in instance.PrefixSlots)
            if (slot.DefinitionId == definitionId) return (slot, instance.PrefixSlots);
        foreach (var slot in instance.SuffixSlots)
            if (slot.DefinitionId == definitionId) return (slot, instance.SuffixSlots);
        foreach (var slot in instance.EnchantSlots)
            if (slot.DefinitionId == definitionId) return (slot, instance.EnchantSlots);
        return (null, null);
    }

    /// <summary>Gets the slot type string for a list of slots within an instance.</summary>
    private static string GetSlotTypeForList(AffixInstanceModel instance, List<AffixSlotModel> list)
    {
        if (ReferenceEquals(list, instance.ImplicitSlots)) return "implicit";
        if (ReferenceEquals(list, instance.PrefixSlots)) return "prefix";
        if (ReferenceEquals(list, instance.SuffixSlots)) return "suffix";
        if (ReferenceEquals(list, instance.EnchantSlots)) return "enchant";
        return "unknown";
    }

    /// <summary>Invalidates pool cache entries for all level buckets of an item class.</summary>
    private async Task InvalidatePoolCacheForItemClassAsync(Guid gameServiceId, string itemClass, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.InvalidatePoolCacheForItemClass");

        // Invalidate known slot types across level buckets
        var slotTypes = new[] { "prefix", "suffix", "enchant" };
        foreach (var slotType in slotTypes)
        {
            for (var bucket = 0; bucket <= _configuration.MaxItemLevel; bucket += _configuration.ItemLevelBucketSize)
            {
                await _poolCache.DeleteAsync(BuildPoolCacheKey(gameServiceId, itemClass, slotType, bucket), ct);
            }
        }
    }

    /// <summary>Enriches affix slots with definition details.</summary>
    private async Task<List<EnrichedAffixSlot>> EnrichSlotsAsync(List<AffixSlotModel> slots, bool isIdentified, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.EnrichSlots");

        var enriched = new List<EnrichedAffixSlot>();
        foreach (var slot in slots)
        {
            var def = await GetDefinitionWithCacheAsync(slot.DefinitionId, ct);
            enriched.Add(new EnrichedAffixSlot
            {
                DefinitionId = slot.DefinitionId,
                DefinitionCode = slot.DefinitionCode,
                ModGroup = slot.ModGroup,
                DisplayName = def?.DisplayName,
                Tier = def?.Tier,
                Category = def?.Category,
                RolledValues = isIdentified ? slot.RolledValues.ToList() : null,
                StatGrants = isIdentified ? def?.StatGrants.ToList() : null,
                IsFractured = slot.IsFractured
            });
        }
        return enriched;
    }

    /// <summary>Computes stats for an instance without caching (used by CompareItems).</summary>
    private async Task<Dictionary<string, double>> ComputeStatsForInstance(AffixInstanceModel instance, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.ComputeStatsForInstance");

        var stats = new Dictionary<string, double>();
        var qualityMod = 1.0 + (instance.Quality / 100.0);
        foreach (var slot in instance.AllSlots())
        {
            var def = await GetDefinitionWithCacheAsync(slot.DefinitionId, ct);
            if (def == null) continue;
            for (var i = 0; i < def.StatGrants.Length && i < slot.RolledValues.Length; i++)
            {
                var sc = def.StatGrants[i].StatCode;
                stats[sc] = stats.GetValueOrDefault(sc) + slot.RolledValues[i] * qualityMod;
            }
        }
        return stats;
    }

    #endregion

    #region Mapping Helpers

    private static AffixDefinitionResponse MapDefinitionToResponse(AffixDefinitionModel model)
        => new()
        {
            DefinitionId = model.DefinitionId,
            GameServiceId = model.GameServiceId,
            Code = model.Code,
            SlotType = model.SlotType,
            ModGroup = model.ModGroup,
            Tier = model.Tier,
            Category = model.Category,
            Tags = model.Tags?.ToList(),
            StatGrants = model.StatGrants.ToList(),
            SpawnWeight = model.SpawnWeight,
            SpawnTagModifiers = model.SpawnTagModifiers?.ToList(),
            RequiredItemLevel = model.RequiredItemLevel,
            RequiredInfluences = model.RequiredInfluences?.ToList(),
            ValidItemClasses = model.ValidItemClasses?.ToList(),
            DisplayName = model.DisplayName,
            DisplayOrder = model.DisplayOrder,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };

    private static AffixDefinitionUpdatedEvent MapDefinitionToUpdatedEvent(AffixDefinitionModel model, List<string> changedFields)
        => new()
        {
            DefinitionId = model.DefinitionId,
            GameServiceId = model.GameServiceId,
            Code = model.Code,
            SlotType = model.SlotType,
            ModGroup = model.ModGroup,
            Tier = model.Tier,
            Category = model.Category,
            Tags = model.Tags?.ToList(),
            StatGrants = model.StatGrants.ToList(),
            SpawnWeight = model.SpawnWeight,
            SpawnTagModifiers = model.SpawnTagModifiers?.ToList(),
            RequiredItemLevel = model.RequiredItemLevel,
            RequiredInfluences = model.RequiredInfluences?.ToList(),
            ValidItemClasses = model.ValidItemClasses?.ToList(),
            DisplayName = model.DisplayName,
            DisplayOrder = model.DisplayOrder,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? model.CreatedAt,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            ChangedFields = changedFields
        };

    private static ImplicitMappingResponse MapImplicitToResponse(ImplicitMappingModel model)
        => new()
        {
            MappingId = model.MappingId,
            GameServiceId = model.GameServiceId,
            ItemTemplateCode = model.ItemTemplateCode,
            ImplicitDefinitionIds = model.ImplicitDefinitionIds.ToList()
        };

    private static AffixInstanceResponse MapInstanceToResponse(AffixInstanceModel model)
        => new()
        {
            ItemInstanceId = model.ItemInstanceId,
            GameServiceId = model.GameServiceId,
            EffectiveRarity = model.EffectiveRarity,
            ItemLevel = model.ItemLevel,
            ImplicitSlots = model.ImplicitSlots.Select(MapSlotModelToData).ToList(),
            PrefixSlots = model.PrefixSlots.Select(MapSlotModelToData).ToList(),
            SuffixSlots = model.SuffixSlots.Select(MapSlotModelToData).ToList(),
            EnchantSlots = model.EnchantSlots.Select(MapSlotModelToData).ToList(),
            Influences = model.Influences.ToList(),
            States = MapStatesToResponse(model.States),
            Quality = model.Quality
        };

    private static AffixSlotData MapSlotModelToData(AffixSlotModel model)
        => new()
        {
            DefinitionId = model.DefinitionId,
            DefinitionCode = model.DefinitionCode,
            ModGroup = model.ModGroup,
            RolledValues = model.RolledValues.ToList(),
            IsFractured = model.IsFractured
        };

    private static AffixSlotModel MapSlotDataToModel(AffixSlotData data)
        => new()
        {
            DefinitionId = data.DefinitionId,
            DefinitionCode = data.DefinitionCode,
            ModGroup = data.ModGroup,
            RolledValues = data.RolledValues.ToArray(),
            IsFractured = data.IsFractured
        };

    private static AffixStates MapStatesToResponse(AffixStatesModel model)
        => new()
        {
            IsCorrupted = model.IsCorrupted,
            IsMirrored = model.IsMirrored,
            IsSplit = model.IsSplit,
            IsIdentified = model.IsIdentified,
            IsSynthesized = model.IsSynthesized
        };

    private static AffixInstanceBatchModifiedEntry MapInstanceToModifiedEntry(AffixInstanceModel model, string[] changedFields)
        => new()
        {
            ItemInstanceId = model.ItemInstanceId,
            GameServiceId = model.GameServiceId,
            EffectiveRarity = model.EffectiveRarity,
            ItemLevel = model.ItemLevel,
            Quality = model.Quality,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? model.CreatedAt,
            ChangedFields = changedFields.ToList()
        };

    #endregion
}
