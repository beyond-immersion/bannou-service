using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Transit.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Transit;

// =============================================================================
// TransitService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by TransitService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (TransitService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ITransitService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (TransitService.Helpers.cs):
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
/// Private and internal helper methods for TransitService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class TransitService
{
    // Move private/internal helper methods here from TransitService.cs
    #region Mode Event Publishing

    /// <summary>
    /// Publishes a transit.mode.created lifecycle event with full entity data.
    /// </summary>
    private async Task PublishModeCreatedEventAsync(TransitModeModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishModeCreatedEventAsync");

        var eventModel = MapModelToModeCreatedEvent(model);

        var published = await _messageBus.PublishTransitModeCreatedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.mode.created event for {ModeCode}", model.Code);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.mode.created event for {ModeCode}", model.Code);
        }
    }

    /// <summary>
    /// Publishes a transit.mode.updated lifecycle event with current state and changed fields.
    /// Used for property updates, deprecation, and undeprecation.
    /// </summary>
    private async Task PublishModeUpdatedEventAsync(TransitModeModel model, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishModeUpdatedEventAsync");

        var changedFieldsList = changedFields.ToList();
        var eventModel = MapModelToModeUpdatedEvent(model, changedFieldsList);

        var published = await _messageBus.PublishTransitModeUpdatedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.mode.updated event for {ModeCode} with changed fields: {ChangedFields}",
                model.Code, string.Join(", ", changedFieldsList));
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.mode.updated event for {ModeCode}", model.Code);
        }
    }

    /// <summary>
    /// Publishes a transit.mode.deleted lifecycle event after mode removal.
    /// </summary>
    private async Task PublishModeDeletedEventAsync(TransitModeModel model, string deletedReason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishModeDeletedEventAsync");

        var eventModel = MapModelToModeDeletedEvent(model, deletedReason);

        var published = await _messageBus.PublishTransitModeDeletedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.mode.deleted event for {ModeCode}", model.Code);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.mode.deleted event for {ModeCode}", model.Code);
        }
    }

    /// <summary>
    /// Maps a TransitModeModel to a TransitModeCreatedEvent.
    /// </summary>
    private static TransitModeCreatedEvent MapModelToModeCreatedEvent(TransitModeModel model)
    {
        return new TransitModeCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            BaseSpeedKmPerGameHour = model.BaseSpeedKmPerGameHour,
            TerrainSpeedModifiers = model.TerrainSpeedModifiers?.Select(t => new TerrainSpeedModifier
            {
                TerrainType = t.TerrainType,
                Multiplier = t.Multiplier
            }).ToList(),
            PassengerCapacity = model.PassengerCapacity,
            CargoCapacityKg = model.CargoCapacityKg,
            CargoSpeedPenaltyRate = model.CargoSpeedPenaltyRate,
            CompatibleTerrainTypes = model.CompatibleTerrainTypes.ToList(),
            ValidEntityTypes = model.ValidEntityTypes?.ToList(),
            Requirements = new TransitModeRequirements
            {
                RequiredItemTag = model.Requirements.RequiredItemTag,
                AllowedSpeciesCodes = model.Requirements.AllowedSpeciesCodes?.ToList(),
                ExcludedSpeciesCodes = model.Requirements.ExcludedSpeciesCodes?.ToList(),
                MinimumPartySize = model.Requirements.MinimumPartySize,
                MaximumEntitySizeCategory = model.Requirements.MaximumEntitySizeCategory
            },
            FatigueRatePerGameHour = model.FatigueRatePerGameHour,
            NoiseLevelNormalized = model.NoiseLevelNormalized,
            RealmRestrictions = model.RealmRestrictions?.ToList(),
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Tags = model.Tags?.ToList(),
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    /// <summary>
    /// Maps a TransitModeModel to a TransitModeUpdatedEvent with changed fields.
    /// </summary>
    private static TransitModeUpdatedEvent MapModelToModeUpdatedEvent(TransitModeModel model, List<string> changedFields)
    {
        return new TransitModeUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            BaseSpeedKmPerGameHour = model.BaseSpeedKmPerGameHour,
            TerrainSpeedModifiers = model.TerrainSpeedModifiers?.Select(t => new TerrainSpeedModifier
            {
                TerrainType = t.TerrainType,
                Multiplier = t.Multiplier
            }).ToList(),
            PassengerCapacity = model.PassengerCapacity,
            CargoCapacityKg = model.CargoCapacityKg,
            CargoSpeedPenaltyRate = model.CargoSpeedPenaltyRate,
            CompatibleTerrainTypes = model.CompatibleTerrainTypes.ToList(),
            ValidEntityTypes = model.ValidEntityTypes?.ToList(),
            Requirements = new TransitModeRequirements
            {
                RequiredItemTag = model.Requirements.RequiredItemTag,
                AllowedSpeciesCodes = model.Requirements.AllowedSpeciesCodes?.ToList(),
                ExcludedSpeciesCodes = model.Requirements.ExcludedSpeciesCodes?.ToList(),
                MinimumPartySize = model.Requirements.MinimumPartySize,
                MaximumEntitySizeCategory = model.Requirements.MaximumEntitySizeCategory
            },
            FatigueRatePerGameHour = model.FatigueRatePerGameHour,
            NoiseLevelNormalized = model.NoiseLevelNormalized,
            RealmRestrictions = model.RealmRestrictions?.ToList(),
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Tags = model.Tags?.ToList(),
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt,
            ChangedFields = changedFields
        };
    }

    /// <summary>
    /// Maps a TransitModeModel to a TransitModeDeletedEvent with deletion reason.
    /// </summary>
    private static TransitModeDeletedEvent MapModelToModeDeletedEvent(TransitModeModel model, string deletedReason)
    {
        return new TransitModeDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            BaseSpeedKmPerGameHour = model.BaseSpeedKmPerGameHour,
            TerrainSpeedModifiers = model.TerrainSpeedModifiers?.Select(t => new TerrainSpeedModifier
            {
                TerrainType = t.TerrainType,
                Multiplier = t.Multiplier
            }).ToList(),
            PassengerCapacity = model.PassengerCapacity,
            CargoCapacityKg = model.CargoCapacityKg,
            CargoSpeedPenaltyRate = model.CargoSpeedPenaltyRate,
            CompatibleTerrainTypes = model.CompatibleTerrainTypes.ToList(),
            ValidEntityTypes = model.ValidEntityTypes?.ToList(),
            Requirements = new TransitModeRequirements
            {
                RequiredItemTag = model.Requirements.RequiredItemTag,
                AllowedSpeciesCodes = model.Requirements.AllowedSpeciesCodes?.ToList(),
                ExcludedSpeciesCodes = model.Requirements.ExcludedSpeciesCodes?.ToList(),
                MinimumPartySize = model.Requirements.MinimumPartySize,
                MaximumEntitySizeCategory = model.Requirements.MaximumEntitySizeCategory
            },
            FatigueRatePerGameHour = model.FatigueRatePerGameHour,
            NoiseLevelNormalized = model.NoiseLevelNormalized,
            RealmRestrictions = model.RealmRestrictions?.ToList(),
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Tags = model.Tags?.ToList(),
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt,
            DeletedReason = deletedReason
        };
    }

    #endregion
    #region Journey Helpers

    /// <summary>
    /// Maps an internal <see cref="TransitJourneyModel"/> to the generated <see cref="TransitJourney"/> API model.
    /// </summary>
    private static TransitJourney MapJourneyToApi(TransitJourneyModel model)
    {
        return new TransitJourney
        {
            Id = model.Id,
            EntityId = model.EntityId,
            EntityType = model.EntityType,
            Legs = model.Legs.Select(l => new TransitJourneyLeg
            {
                ConnectionId = l.ConnectionId,
                FromLocationId = l.FromLocationId,
                ToLocationId = l.ToLocationId,
                ModeCode = l.ModeCode,
                DistanceKm = l.DistanceKm,
                TerrainType = l.TerrainType,
                EstimatedDurationGameHours = l.EstimatedDurationGameHours,
                WaypointTransferTimeGameHours = l.WaypointTransferTimeGameHours,
                Status = l.Status,
                CompletedAtGameTime = l.CompletedAtGameTime
            }).ToList(),
            CurrentLegIndex = model.CurrentLegIndex,
            PrimaryModeCode = model.PrimaryModeCode,
            EffectiveSpeedKmPerGameHour = model.EffectiveSpeedKmPerGameHour,
            PlannedDepartureGameTime = model.PlannedDepartureGameTime,
            ActualDepartureGameTime = model.ActualDepartureGameTime,
            EstimatedArrivalGameTime = model.EstimatedArrivalGameTime,
            ActualArrivalGameTime = model.ActualArrivalGameTime,
            OriginLocationId = model.OriginLocationId,
            DestinationLocationId = model.DestinationLocationId,
            CurrentLocationId = model.CurrentLocationId,
            Status = model.Status,
            StatusReason = model.StatusReason,
            Interruptions = model.Interruptions.Select(i => new TransitInterruption
            {
                LegIndex = i.LegIndex,
                GameTime = i.GameTime,
                Reason = i.Reason,
                // API schema uses non-nullable decimal (0 for unresolved); internal model uses null for unknown duration
                DurationGameHours = i.DurationGameHours ?? 0,
                Resolved = i.Resolved
            }).ToList(),
            PartySize = model.PartySize,
            CargoWeightKg = model.CargoWeightKg,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    /// <summary>
    /// Maps a <see cref="JourneyArchiveModel"/> to the generated <see cref="TransitJourney"/> API model.
    /// </summary>
    private static TransitJourney MapArchivedJourneyToApi(JourneyArchiveModel model)
    {
        return new TransitJourney
        {
            Id = model.Id,
            EntityId = model.EntityId,
            EntityType = model.EntityType,
            Legs = model.Legs.Select(l => new TransitJourneyLeg
            {
                ConnectionId = l.ConnectionId,
                FromLocationId = l.FromLocationId,
                ToLocationId = l.ToLocationId,
                ModeCode = l.ModeCode,
                DistanceKm = l.DistanceKm,
                TerrainType = l.TerrainType,
                EstimatedDurationGameHours = l.EstimatedDurationGameHours,
                WaypointTransferTimeGameHours = l.WaypointTransferTimeGameHours,
                Status = l.Status,
                CompletedAtGameTime = l.CompletedAtGameTime
            }).ToList(),
            CurrentLegIndex = model.CurrentLegIndex,
            PrimaryModeCode = model.PrimaryModeCode,
            EffectiveSpeedKmPerGameHour = model.EffectiveSpeedKmPerGameHour,
            PlannedDepartureGameTime = model.PlannedDepartureGameTime,
            ActualDepartureGameTime = model.ActualDepartureGameTime,
            EstimatedArrivalGameTime = model.EstimatedArrivalGameTime,
            ActualArrivalGameTime = model.ActualArrivalGameTime,
            OriginLocationId = model.OriginLocationId,
            DestinationLocationId = model.DestinationLocationId,
            CurrentLocationId = model.CurrentLocationId,
            Status = model.Status,
            StatusReason = model.StatusReason,
            Interruptions = model.Interruptions.Select(i => new TransitInterruption
            {
                LegIndex = i.LegIndex,
                GameTime = i.GameTime,
                Reason = i.Reason,
                // API schema uses non-nullable decimal (0 for unresolved); internal model uses null for unknown duration
                DurationGameHours = i.DurationGameHours ?? 0,
                Resolved = i.Resolved
            }).ToList(),
            PartySize = model.PartySize,
            CargoWeightKg = model.CargoWeightKg,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    /// <summary>
    /// Computes the effective speed for a mode accounting for cargo weight penalty.
    /// Uses the cargo speed penalty formula from the deep dive:
    /// speed_reduction = (cargo - threshold) / (capacity - threshold) x rate
    /// </summary>
    private decimal ComputeEffectiveSpeed(TransitModeModel mode, decimal cargoWeightKg)
    {
        var baseSpeed = mode.BaseSpeedKmPerGameHour;

        if (cargoWeightKg <= 0 || mode.CargoCapacityKg <= 0)
        {
            return baseSpeed;
        }

        var threshold = (decimal)_configuration.CargoSpeedPenaltyThresholdKg;
        if (cargoWeightKg <= threshold)
        {
            return baseSpeed;
        }

        var rate = mode.CargoSpeedPenaltyRate ?? (decimal)_configuration.DefaultCargoSpeedPenaltyRate;
        var capacity = mode.CargoCapacityKg;

        if (capacity <= threshold)
        {
            return baseSpeed;
        }

        var penaltyFraction = (cargoWeightKg - threshold) / (capacity - threshold);
        penaltyFraction = Math.Min(penaltyFraction, 1m); // Clamp to 100% of capacity
        var speedReduction = penaltyFraction * rate;

        return baseSpeed * (1m - speedReduction);
    }

    /// <summary>
    /// Computes the estimated duration in game-hours for a single journey leg,
    /// accounting for terrain speed modifiers and cargo penalties.
    /// </summary>
    private decimal ComputeLegDurationGameHours(
        decimal distanceKm,
        string terrainType,
        TransitModeModel legMode,
        decimal cargoWeightKg)
    {
        var effectiveSpeed = ComputeEffectiveSpeed(legMode, cargoWeightKg);

        // Apply terrain speed modifier
        if (legMode.TerrainSpeedModifiers != null && !string.IsNullOrEmpty(terrainType))
        {
            var terrainMod = legMode.TerrainSpeedModifiers
                .FirstOrDefault(t => t.TerrainType == terrainType);
            if (terrainMod != null)
            {
                effectiveSpeed *= terrainMod.Multiplier;
            }
        }

        // Avoid division by zero
        if (effectiveSpeed <= 0)
        {
            effectiveSpeed = (decimal)_configuration.DefaultWalkingSpeedKmPerGameHour;
        }

        return distanceKm / effectiveSpeed;
    }

    /// <summary>
    /// Reports an entity's position to the Location service if AutoUpdateLocationOnTransition is enabled.
    /// Best-effort: failures are logged but do not fail the journey operation.
    /// </summary>
    private async Task TryReportEntityPositionAsync(
        string entityType,
        Guid entityId,
        Guid locationId,
        Guid? previousLocationId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.TryReportEntityPositionAsync");

        if (!_configuration.AutoUpdateLocationOnTransition)
        {
            return;
        }

        try
        {
            await _locationClient.ReportEntityPositionAsync(
                new Location.ReportEntityPositionRequest
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    LocationId = locationId,
                    PreviousLocationId = previousLocationId,
                    ReportedBy = "transit"
                },
                cancellationToken);

            _logger.LogDebug("Reported entity {EntityId} position at location {LocationId}", entityId, locationId);
        }
        catch (ApiException ex)
        {
            // Best-effort: position reporting failure should not fail the journey operation
            _logger.LogWarning(ex, "Failed to report entity {EntityId} position at location {LocationId}", entityId, locationId);
        }
    }

    /// <summary>
    /// Attempts to auto-reveal a discoverable connection for an entity after traversing it.
    /// Only reveals if the connection is marked as discoverable.
    /// Best-effort: failures are logged but do not fail the advance operation.
    /// </summary>
    private async Task TryAutoRevealDiscoveryAsync(Guid entityId, Guid connectionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.TryAutoRevealDiscoveryAsync");

        try
        {
            var connKey = BuildConnectionKey(connectionId);
            var conn = await _connectionStore.GetAsync(connKey, cancellationToken: cancellationToken);
            if (conn == null || !conn.Discoverable)
            {
                return;
            }

            // Call RevealDiscovery internally (reuses the full discovery logic including events)
            var revealRequest = new RevealDiscoveryRequest
            {
                EntityId = entityId,
                ConnectionId = connectionId,
                Source = "travel"
            };

            await RevealDiscoveryAsync(revealRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort: auto-reveal failure should not fail the advance operation
            _logger.LogWarning(ex, "Failed to auto-reveal connection {ConnectionId} for entity {EntityId}", connectionId, entityId);
        }
    }

    /// <summary>
    /// Resolves the realm ID for a location. Returns null if the location is not found.
    /// Best-effort helper for event publishing -- does not fail the calling operation.
    /// </summary>
    private async Task<Guid?> ResolveLocationRealmIdAsync(Guid locationId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ResolveLocationRealmIdAsync");

        try
        {
            var location = await _locationClient.GetLocationAsync(
                new Location.GetLocationRequest { LocationId = locationId },
                cancellationToken);
            return location.RealmId;
        }
        catch (ApiException)
        {
            _logger.LogDebug("Could not resolve realm for location {LocationId}", locationId);
            return null;
        }
    }

    /// <summary>
    /// Adds a journey ID to the journey index list in Redis. Used by the
    /// <see cref="JourneyArchivalWorker"/> to discover journeys without Redis SCAN.
    /// Uses optimistic concurrency to handle concurrent additions safely.
    /// </summary>
    /// <param name="journeyId">The journey ID to add to the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task AddToJourneyIndexAsync(Guid journeyId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.AddToJourneyIndexAsync");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (currentIndex, etag) = await _journeyIndexStore.GetWithETagAsync(JOURNEY_INDEX_KEY, cancellationToken);

            var updatedIndex = currentIndex ?? new List<Guid>();
            updatedIndex.Add(journeyId);

            if (etag == null)
            {
                // First entry -- no concurrency conflict possible
                await _journeyIndexStore.SaveAsync(JOURNEY_INDEX_KEY, updatedIndex, cancellationToken: cancellationToken);
                return;
            }

            var result = await _journeyIndexStore.TrySaveAsync(JOURNEY_INDEX_KEY, updatedIndex, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on journey index during add, retrying (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to add journey {JourneyId} to index after retries - archival worker will miss this journey until it is re-indexed", journeyId);
    }

    #endregion
    #region Journey Event Publishing

    /// <summary>
    /// Publishes a transit.journey.departed event.
    /// </summary>
    private async Task PublishJourneyDepartedEventAsync(
        TransitJourneyModel journey,
        Guid? originRealmId,
        Guid? destinationRealmId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyDepartedEventAsync");

        var eventModel = new TransitJourneyDepartedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            OriginLocationId = journey.OriginLocationId,
            DestinationLocationId = journey.DestinationLocationId,
            PrimaryModeCode = journey.PrimaryModeCode,
            EstimatedArrivalGameTime = journey.EstimatedArrivalGameTime,
            PartySize = journey.PartySize,
            OriginRealmId = originRealmId,
            DestinationRealmId = destinationRealmId,
            CrossRealm = originRealmId.HasValue && destinationRealmId.HasValue && originRealmId != destinationRealmId
        };

        var published = await _messageBus.PublishTransitJourneyDepartedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.departed event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.departed event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.journey.waypoint-reached event.
    /// </summary>
    private async Task PublishJourneyWaypointReachedEventAsync(
        TransitJourneyModel journey,
        Guid waypointLocationId,
        Guid nextLocationId,
        int completedLegIndex,
        Guid connectionId,
        Guid? realmId,
        bool crossedRealmBoundary,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyWaypointReachedEventAsync");

        var eventModel = new TransitJourneyWaypointReachedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            WaypointLocationId = waypointLocationId,
            NextLocationId = nextLocationId,
            LegIndex = completedLegIndex,
            RemainingLegs = journey.Legs.Count - journey.CurrentLegIndex,
            ConnectionId = connectionId,
            RealmId = realmId,
            CrossedRealmBoundary = crossedRealmBoundary
        };

        var published = await _messageBus.PublishTransitJourneyWaypointReachedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.waypoint-reached event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.waypoint-reached event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.journey.arrived event.
    /// </summary>
    private async Task PublishJourneyArrivedEventAsync(
        TransitJourneyModel journey,
        Guid? originRealmId,
        Guid? destinationRealmId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyArrivedEventAsync");

        var totalDistanceKm = journey.Legs.Where(l => l.Status == JourneyLegStatus.Completed).Sum(l => l.DistanceKm);
        var totalGameHours = journey.ActualArrivalGameTime.HasValue && journey.ActualDepartureGameTime.HasValue
            ? journey.ActualArrivalGameTime.Value - journey.ActualDepartureGameTime.Value
            : journey.Legs.Sum(l => l.EstimatedDurationGameHours);

        var eventModel = new TransitJourneyArrivedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            OriginLocationId = journey.OriginLocationId,
            DestinationLocationId = journey.DestinationLocationId,
            PrimaryModeCode = journey.PrimaryModeCode,
            TotalGameHours = totalGameHours,
            TotalDistanceKm = totalDistanceKm,
            InterruptionCount = journey.Interruptions.Count,
            LegsCompleted = journey.Legs.Count(l => l.Status == JourneyLegStatus.Completed),
            OriginRealmId = originRealmId,
            DestinationRealmId = destinationRealmId,
            CrossRealm = originRealmId.HasValue && destinationRealmId.HasValue && originRealmId != destinationRealmId
        };

        var published = await _messageBus.PublishTransitJourneyArrivedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.arrived event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.arrived event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.journey.interrupted event.
    /// </summary>
    private async Task PublishJourneyInterruptedEventAsync(
        TransitJourneyModel journey,
        Guid? realmId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyInterruptedEventAsync");

        var eventModel = new TransitJourneyInterruptedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            CurrentLocationId = journey.CurrentLocationId,
            CurrentLegIndex = journey.CurrentLegIndex,
            Reason = journey.StatusReason,
            RealmId = realmId
        };

        var published = await _messageBus.PublishTransitJourneyInterruptedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.interrupted event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.interrupted event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.journey.resumed event.
    /// </summary>
    private async Task PublishJourneyResumedEventAsync(
        TransitJourneyModel journey,
        Guid? realmId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyResumedEventAsync");

        var currentLegModeCode = journey.CurrentLegIndex < journey.Legs.Count
            ? journey.Legs[journey.CurrentLegIndex].ModeCode
            : journey.PrimaryModeCode;

        var eventModel = new TransitJourneyResumedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            CurrentLocationId = journey.CurrentLocationId,
            DestinationLocationId = journey.DestinationLocationId,
            CurrentLegIndex = journey.CurrentLegIndex,
            RemainingLegs = journey.Legs.Count - journey.CurrentLegIndex,
            ModeCode = currentLegModeCode,
            RealmId = realmId
        };

        var published = await _messageBus.PublishTransitJourneyResumedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.resumed event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.resumed event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.journey.abandoned event.
    /// </summary>
    private async Task PublishJourneyAbandonedEventAsync(
        TransitJourneyModel journey,
        Guid? originRealmId,
        Guid? destinationRealmId,
        Guid? abandonedAtRealmId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyAbandonedEventAsync");

        var eventModel = new TransitJourneyAbandonedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            OriginLocationId = journey.OriginLocationId,
            DestinationLocationId = journey.DestinationLocationId,
            AbandonedAtLocationId = journey.CurrentLocationId,
            Reason = journey.StatusReason,
            CompletedLegs = journey.Legs.Count(l => l.Status == JourneyLegStatus.Completed),
            TotalLegs = journey.Legs.Count,
            OriginRealmId = originRealmId,
            DestinationRealmId = destinationRealmId,
            AbandonedAtRealmId = abandonedAtRealmId,
            CrossRealm = originRealmId.HasValue && destinationRealmId.HasValue && originRealmId != destinationRealmId
        };

        var published = await _messageBus.PublishTransitJourneyAbandonedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.abandoned event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.abandoned event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a TransitJourneyUpdated client event to the traveling entity's WebSocket session(s)
    /// via IEntitySessionRegistry.
    /// </summary>
    /// <remarks>
    /// Pushes journey state changes (departure, waypoint, arrival, interruption, abandonment)
    /// to the entity's bound sessions for real-time UI updates.
    /// </remarks>
    /// <param name="journey">The journey model after the state change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishJourneyUpdatedClientEventAsync(
        TransitJourneyModel journey,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyUpdatedClientEventAsync");

        var remainingLegs = journey.Legs.Count - journey.CurrentLegIndex;

        var clientEvent = new TransitJourneyUpdatedClientEvent
        {
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            Status = journey.Status,
            CurrentLocationId = journey.CurrentLocationId,
            DestinationLocationId = journey.DestinationLocationId,
            EstimatedArrivalGameTime = journey.EstimatedArrivalGameTime,
            CurrentLegIndex = journey.CurrentLegIndex,
            RemainingLegs = remainingLegs,
            PrimaryModeCode = journey.PrimaryModeCode
        };

        var count = await _entitySessionRegistry.PublishToEntitySessionsAsync(
            journey.EntityType, journey.EntityId, clientEvent, cancellationToken);

        _logger.LogDebug(
            "Published transit.journey_updated to {SessionCount} sessions for entity {EntityId}, journey {JourneyId}: {Status}",
            count, journey.EntityId, journey.Id, journey.Status);
    }

    #endregion
    /// <summary>
    /// Updates the Redis discovery cache for an entity after a new discovery.
    /// Loads the existing set, adds the new connection ID, and saves with TTL.
    /// </summary>
    /// <remarks>
    /// This is a best-effort cache optimization. The read-modify-write on the cache set is
    /// non-atomic: concurrent cache updates for the same entity could lose entries. This is
    /// acceptable because the cache is rebuilt from the authoritative MySQL discovery store
    /// on cache miss. The route calculator falls back to MySQL when the cache is absent or stale.
    /// </remarks>
    /// <param name="entityId">The entity whose discovery cache should be updated.</param>
    /// <param name="connectionId">The newly discovered connection ID to add to the cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task UpdateDiscoveryCacheAsync(Guid entityId, Guid connectionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.UpdateDiscoveryCacheAsync");

        var ttlSeconds = _configuration.DiscoveryCacheTtlSeconds;
        if (ttlSeconds <= 0)
        {
            _logger.LogDebug("Discovery cache TTL is 0, skipping cache update for entity {EntityId}", entityId);
            return;
        }

        var cacheKey = BuildDiscoveryCacheKey(entityId);
        var cachedSet = await _discoveryCacheStore.GetAsync(cacheKey, cancellationToken: cancellationToken) ?? new HashSet<Guid>();
        cachedSet.Add(connectionId);

        await _discoveryCacheStore.SaveAsync(cacheKey, cachedSet, new StateOptions { Ttl = ttlSeconds }, cancellationToken);
    }

    /// <summary>
    /// Maps an internal <see cref="TransitDiscoveryModel"/> to the generated <see cref="DiscoveryRecord"/> API model.
    /// </summary>
    /// <param name="model">The internal discovery storage model.</param>
    /// <param name="isNew">Whether this discovery is new (first time) or a re-revelation.</param>
    /// <returns>The API-facing discovery record.</returns>
    private static DiscoveryRecord MapDiscoveryToApi(TransitDiscoveryModel model, bool isNew)
    {
        return new DiscoveryRecord
        {
            EntityId = model.EntityId,
            ConnectionId = model.ConnectionId,
            Source = model.Source,
            DiscoveredAt = model.DiscoveredAt,
            IsNew = isNew
        };
    }
    #region Connection Helpers

    /// <summary>
    /// Maps an internal <see cref="TransitConnectionModel"/> to the generated <see cref="TransitConnection"/> API model.
    /// </summary>
    /// <param name="model">The internal connection storage model.</param>
    /// <returns>The API-facing connection model.</returns>
    private static TransitConnection MapConnectionToApi(TransitConnectionModel model)
    {
        return new TransitConnection
        {
            Id = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            Bidirectional = model.Bidirectional,
            DistanceKm = model.DistanceKm,
            TerrainType = model.TerrainType,
            CompatibleModes = model.CompatibleModes.ToList(),
            SeasonalAvailability = model.SeasonalAvailability?.Select(s => new SeasonalAvailabilityEntry
            {
                Season = s.Season,
                Available = s.Available
            }).ToList(),
            BaseRiskLevel = model.BaseRiskLevel,
            RiskDescription = model.RiskDescription,
            Status = model.Status,
            StatusReason = model.StatusReason,
            StatusChangedAt = model.StatusChangedAt,
            Discoverable = model.Discoverable,
            Name = model.Name,
            Code = model.Code,
            Tags = model.Tags?.ToList(),
            FromRealmId = model.FromRealmId,
            ToRealmId = model.ToRealmId,
            CrossRealm = model.CrossRealm,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    /// <summary>
    /// Validates that seasonal availability season keys match valid season codes
    /// from the Worldstate calendar system. For cross-realm connections, validates
    /// against both realms' calendars.
    /// </summary>
    /// <remarks>
    /// This validation uses a best-effort approach: it queries Worldstate's calendar
    /// templates for the affected realms. If calendars are not yet configured, validation
    /// passes with a warning (seasonal connections may be created before calendars).
    /// Full calendar-aware enforcement happens at journey creation time and via the
    /// seasonal connection worker (Phase 8).
    /// </remarks>
    /// <param name="seasonalAvailability">The seasonal availability entries to validate.</param>
    /// <param name="fromRealmId">The source realm ID.</param>
    /// <param name="toRealmId">The destination realm ID.</param>
    /// <param name="crossRealm">Whether this is a cross-realm connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if validation passes, false if invalid season keys detected.</returns>
    private async Task<bool> ValidateSeasonalKeysAsync(
        ICollection<SeasonalAvailabilityEntry> seasonalAvailability,
        Guid fromRealmId,
        Guid toRealmId,
        bool crossRealm,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ValidateSeasonalKeysAsync");

        // Validate basic format: season codes must be non-empty
        foreach (var entry in seasonalAvailability)
        {
            if (string.IsNullOrWhiteSpace(entry.Season))
            {
                _logger.LogDebug("Invalid seasonal availability: empty season code");
                return false;
            }
        }

        // Check for duplicate season codes
        var seasonCodes = seasonalAvailability.Select(s => s.Season).ToList();
        if (seasonCodes.Distinct().Count() != seasonCodes.Count)
        {
            _logger.LogDebug("Invalid seasonal availability: duplicate season codes");
            return false;
        }

        // Attempt Worldstate calendar validation (best-effort)
        // The calendar lookup requires a gameServiceId which we don't have directly from locations.
        // Phase 8's seasonal worker will enforce calendar-aware validation.
        // For now, we validate format and uniqueness but log a warning about deferred calendar validation.
        _logger.LogDebug("Seasonal availability entries accepted with format validation. Calendar-aware validation deferred to seasonal worker (Phase 8) for realms {FromRealmId}/{ToRealmId}",
            fromRealmId, crossRealm ? toRealmId : fromRealmId);

        await Task.CompletedTask;
        return true;
    }

    /// <summary>
    /// Applies non-null field updates from an <see cref="UpdateConnectionRequest"/> to an existing connection model.
    /// Validates mode codes if compatibleModes is being updated.
    /// Returns the list of field names that were actually changed, or null if validation failed.
    /// </summary>
    /// <param name="model">The connection model to update in place.</param>
    /// <param name="request">The update request containing fields to apply.</param>
    /// <param name="cancellationToken">Cancellation token for async validation.</param>
    /// <returns>List of camelCase field names that were changed, or null if validation failed (caller should return BadRequest).</returns>
    private async Task<List<string>?> ApplyConnectionFieldUpdatesAsync(
        TransitConnectionModel model,
        UpdateConnectionRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ApplyConnectionFieldUpdatesAsync");

        var changedFields = new List<string>();

        if (request.DistanceKm.HasValue && request.DistanceKm.Value != model.DistanceKm)
        {
            model.DistanceKm = request.DistanceKm.Value;
            changedFields.Add("distanceKm");
        }

        if (request.TerrainType != null && request.TerrainType != model.TerrainType)
        {
            model.TerrainType = request.TerrainType;
            changedFields.Add("terrainType");
        }

        if (request.CompatibleModes != null)
        {
            // Validate all new mode codes exist
            foreach (var modeCode in request.CompatibleModes)
            {
                var modeKey = BuildModeKey(modeCode);
                var mode = await _modeStore.GetAsync(modeKey, cancellationToken: cancellationToken);
                if (mode == null)
                {
                    _logger.LogDebug("Invalid mode code in compatible modes update: {ModeCode}", modeCode);
                    // Return null to signal validation failure -- caller returns BadRequest
                    return null;
                }
            }

            model.CompatibleModes = request.CompatibleModes.ToList();
            changedFields.Add("compatibleModes");
        }

        if (request.SeasonalAvailability != null)
        {
            model.SeasonalAvailability = request.SeasonalAvailability.Select(s => new SeasonalAvailabilityModel
            {
                Season = s.Season,
                Available = s.Available
            }).ToList();
            changedFields.Add("seasonalAvailability");
        }

        if (request.BaseRiskLevel.HasValue && request.BaseRiskLevel.Value != model.BaseRiskLevel)
        {
            model.BaseRiskLevel = request.BaseRiskLevel.Value;
            changedFields.Add("baseRiskLevel");
        }

        if (request.RiskDescription != null && request.RiskDescription != model.RiskDescription)
        {
            model.RiskDescription = request.RiskDescription;
            changedFields.Add("riskDescription");
        }

        if (request.Discoverable.HasValue && request.Discoverable.Value != model.Discoverable)
        {
            model.Discoverable = request.Discoverable.Value;
            changedFields.Add("discoverable");
        }

        if (request.Name != null && request.Name != model.Name)
        {
            model.Name = request.Name;
            changedFields.Add("name");
        }

        if (request.Code != null && request.Code != model.Code)
        {
            // Check uniqueness of new code
            if (!string.IsNullOrEmpty(request.Code))
            {
                var existingByCode = await _connectionStore.QueryAsync(
                    c => c.Code == request.Code && c.Id != model.Id,
                    cancellationToken);
                if (existingByCode.Count > 0)
                {
                    _logger.LogDebug("Cannot update connection code: code {Code} already in use", request.Code);
                    // Return null to signal validation failure -- caller returns Conflict
                    return null;
                }
            }

            model.Code = request.Code;
            changedFields.Add("code");
        }

        if (request.Tags != null)
        {
            model.Tags = request.Tags.ToList();
            changedFields.Add("tags");
        }

        return changedFields;
    }

    /// <summary>
    /// Maps a <see cref="SettableConnectionStatus"/> (API enum without seasonal_closed)
    /// to the full <see cref="ConnectionStatus"/> enum.
    /// </summary>
    /// <param name="settableStatus">The settable status from the API request.</param>
    /// <returns>The corresponding full connection status.</returns>
    private static ConnectionStatus MapSettableToConnectionStatus(SettableConnectionStatus settableStatus)
    {
        return settableStatus switch
        {
            SettableConnectionStatus.Open => ConnectionStatus.Open,
            SettableConnectionStatus.Closed => ConnectionStatus.Closed,
            SettableConnectionStatus.Dangerous => ConnectionStatus.Dangerous,
            SettableConnectionStatus.Blocked => ConnectionStatus.Blocked,
            _ => throw new ArgumentOutOfRangeException(nameof(settableStatus), settableStatus, "Unknown settable connection status")
        };
    }

    #endregion
    #region Connection Event Publishing

    /// <summary>
    /// Publishes a transit.connection.created lifecycle event with full entity data.
    /// </summary>
    /// <param name="model">The newly created connection model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishConnectionCreatedEventAsync(TransitConnectionModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishConnectionCreatedEventAsync");

        var eventModel = new TransitConnectionCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Id = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            Bidirectional = model.Bidirectional,
            DistanceKm = model.DistanceKm,
            TerrainType = model.TerrainType,
            CompatibleModes = model.CompatibleModes.ToList(),
            SeasonalAvailability = model.SeasonalAvailability?.Select(s => new SeasonalAvailabilityEntry
            {
                Season = s.Season,
                Available = s.Available
            }).ToList(),
            BaseRiskLevel = model.BaseRiskLevel,
            RiskDescription = model.RiskDescription,
            Status = model.Status,
            StatusReason = model.StatusReason,
            StatusChangedAt = model.StatusChangedAt,
            Discoverable = model.Discoverable,
            Name = model.Name,
            Code = model.Code,
            Tags = model.Tags?.ToList(),
            FromRealmId = model.FromRealmId,
            ToRealmId = model.ToRealmId,
            CrossRealm = model.CrossRealm,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };

        var published = await _messageBus.PublishTransitConnectionCreatedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.connection.created event for {ConnectionId}", model.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.connection.created event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.connection.updated lifecycle event with current state and changed fields.
    /// </summary>
    /// <param name="model">The updated connection model.</param>
    /// <param name="changedFields">List of field names that changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishConnectionUpdatedEventAsync(
        TransitConnectionModel model,
        IEnumerable<string> changedFields,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishConnectionUpdatedEventAsync");

        var changedFieldsList = changedFields.ToList();
        var eventModel = new TransitConnectionUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Id = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            Bidirectional = model.Bidirectional,
            DistanceKm = model.DistanceKm,
            TerrainType = model.TerrainType,
            CompatibleModes = model.CompatibleModes.ToList(),
            SeasonalAvailability = model.SeasonalAvailability?.Select(s => new SeasonalAvailabilityEntry
            {
                Season = s.Season,
                Available = s.Available
            }).ToList(),
            BaseRiskLevel = model.BaseRiskLevel,
            RiskDescription = model.RiskDescription,
            Status = model.Status,
            StatusReason = model.StatusReason,
            StatusChangedAt = model.StatusChangedAt,
            Discoverable = model.Discoverable,
            Name = model.Name,
            Code = model.Code,
            Tags = model.Tags?.ToList(),
            FromRealmId = model.FromRealmId,
            ToRealmId = model.ToRealmId,
            CrossRealm = model.CrossRealm,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt,
            ChangedFields = changedFieldsList
        };

        var published = await _messageBus.PublishTransitConnectionUpdatedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.connection.updated event for {ConnectionId} with changed fields: {ChangedFields}",
                model.Id, string.Join(", ", changedFieldsList));
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.connection.updated event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.connection.deleted lifecycle event with full entity data and deletion reason.
    /// </summary>
    /// <param name="model">The deleted connection model (state before deletion).</param>
    /// <param name="deletedReason">Reason for deletion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishConnectionDeletedEventAsync(
        TransitConnectionModel model,
        string deletedReason,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishConnectionDeletedEventAsync");

        var eventModel = new TransitConnectionDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Id = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            Bidirectional = model.Bidirectional,
            DistanceKm = model.DistanceKm,
            TerrainType = model.TerrainType,
            CompatibleModes = model.CompatibleModes.ToList(),
            SeasonalAvailability = model.SeasonalAvailability?.Select(s => new SeasonalAvailabilityEntry
            {
                Season = s.Season,
                Available = s.Available
            }).ToList(),
            BaseRiskLevel = model.BaseRiskLevel,
            RiskDescription = model.RiskDescription,
            Status = model.Status,
            StatusReason = model.StatusReason,
            StatusChangedAt = model.StatusChangedAt,
            Discoverable = model.Discoverable,
            Name = model.Name,
            Code = model.Code,
            Tags = model.Tags?.ToList(),
            FromRealmId = model.FromRealmId,
            ToRealmId = model.ToRealmId,
            CrossRealm = model.CrossRealm,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt,
            DeletedReason = deletedReason
        };

        var published = await _messageBus.PublishTransitConnectionDeletedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.connection.deleted event for {ConnectionId}", model.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.connection.deleted event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.connection.status-changed custom event (not lifecycle).
    /// This event is published on any status transition, including seasonal worker updates.
    /// </summary>
    /// <param name="model">The connection after status change.</param>
    /// <param name="previousStatus">The status before the change.</param>
    /// <param name="forceUpdated">Whether this was a force update (e.g., from seasonal worker).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishConnectionStatusChangedEventAsync(
        TransitConnectionModel model,
        ConnectionStatus previousStatus,
        bool forceUpdated,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishConnectionStatusChangedEventAsync");

        var eventModel = new TransitConnectionStatusChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ConnectionId = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            PreviousStatus = previousStatus,
            NewStatus = model.Status,
            Reason = model.StatusReason,
            ForceUpdated = forceUpdated,
            FromRealmId = model.FromRealmId,
            ToRealmId = model.ToRealmId,
            CrossRealm = model.CrossRealm
        };

        var published = await _messageBus.PublishTransitConnectionStatusChangedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.connection.status-changed event for {ConnectionId}: {PreviousStatus} -> {NewStatus}",
                model.Id, previousStatus, model.Status);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.connection.status-changed event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a TransitConnectionStatusChanged client event to WebSocket sessions
    /// in the affected realm(s) via IEntitySessionRegistry.
    /// </summary>
    /// <remarks>
    /// Routes client events to all sessions registered for the affected realm entity.
    /// Cross-realm connections publish to both the origin and destination realms.
    /// </remarks>
    /// <param name="model">The connection after status change.</param>
    /// <param name="previousStatus">The status before the change.</param>
    /// <param name="reason">Reason for the status change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishConnectionStatusChangedClientEventAsync(
        TransitConnectionModel model,
        ConnectionStatus previousStatus,
        string reason,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishConnectionStatusChangedClientEventAsync");

        var clientEvent = new TransitConnectionStatusChangedClientEvent
        {
            ConnectionId = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            PreviousStatus = previousStatus,
            NewStatus = model.Status,
            Reason = reason
        };

        // Publish to all sessions watching the origin realm
        var fromCount = await _entitySessionRegistry.PublishToEntitySessionsAsync(
            "realm", model.FromRealmId, clientEvent, cancellationToken);

        _logger.LogDebug(
            "Published transit.connection_status_changed to {SessionCount} sessions for realm {RealmId}, connection {ConnectionId}: {PreviousStatus} -> {NewStatus}",
            fromCount, model.FromRealmId, model.Id, previousStatus, model.Status);

        // Cross-realm connections: also publish to the destination realm
        if (model.CrossRealm)
        {
            var toCount = await _entitySessionRegistry.PublishToEntitySessionsAsync(
                "realm", model.ToRealmId, clientEvent, cancellationToken);

            _logger.LogDebug(
                "Published transit.connection_status_changed to {SessionCount} sessions for destination realm {RealmId}, connection {ConnectionId}",
                toCount, model.ToRealmId, model.Id);
        }
    }

    #endregion
    #region Discovery Event Publishing

    /// <summary>
    /// Publishes a transit.discovery.revealed service bus event when a connection
    /// is revealed to an entity for the first time. Consumed by Collection (unlocks),
    /// Quest (objectives), and Analytics (aggregation).
    /// </summary>
    /// <param name="discovery">The discovery record.</param>
    /// <param name="connection">The connection that was discovered (for realm and location data).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishDiscoveryRevealedEventAsync(
        TransitDiscoveryModel discovery,
        TransitConnectionModel connection,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishDiscoveryRevealedEventAsync");

        var eventModel = new TransitDiscoveryRevealedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityId = discovery.EntityId,
            ConnectionId = discovery.ConnectionId,
            FromLocationId = connection.FromLocationId,
            ToLocationId = connection.ToLocationId,
            Source = discovery.Source,
            FromRealmId = connection.FromRealmId,
            ToRealmId = connection.ToRealmId,
            CrossRealm = connection.CrossRealm
        };

        var published = await _messageBus.PublishTransitDiscoveryRevealedAsync(eventModel, cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.discovery.revealed event for entity {EntityId} connection {ConnectionId}",
                discovery.EntityId, discovery.ConnectionId);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.discovery.revealed event for entity {EntityId} connection {ConnectionId}",
                discovery.EntityId, discovery.ConnectionId);
        }
    }

    /// <summary>
    /// Publishes a TransitDiscoveryRevealed client event to the discovering entity's WebSocket
    /// session(s) via IEntitySessionRegistry.
    /// </summary>
    /// <remarks>
    /// Discovery reveals are entity-scoped: the client event is pushed to all sessions
    /// registered for the discovering entity. The entity type is determined from the journey
    /// context when auto-revealed during travel, or defaults to "character" for direct
    /// API reveals (the primary discovery use case).
    /// </remarks>
    /// <param name="discovery">The discovery record containing entity and connection IDs.</param>
    /// <param name="connection">The connection that was discovered (for location data and name).</param>
    /// <param name="entityType">The entity type of the discovering entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishDiscoveryRevealedClientEventAsync(
        TransitDiscoveryModel discovery,
        TransitConnectionModel connection,
        string entityType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishDiscoveryRevealedClientEventAsync");

        var clientEvent = new TransitDiscoveryRevealedClientEvent
        {
            ConnectionId = discovery.ConnectionId,
            FromLocationId = connection.FromLocationId,
            ToLocationId = connection.ToLocationId,
            Source = discovery.Source,
            ConnectionName = connection.Name
        };

        var count = await _entitySessionRegistry.PublishToEntitySessionsAsync(
            entityType, discovery.EntityId, clientEvent, cancellationToken);

        _logger.LogDebug(
            "Published transit.discovery_revealed to {SessionCount} sessions for entity {EntityId}, connection {ConnectionId}",
            count, discovery.EntityId, discovery.ConnectionId);
    }

    #endregion
    #region Redis Journey Scan Helpers

    /// <summary>
    /// Scans all active Redis journeys and checks if any use the given mode code.
    /// Returns true if a conflict is found (an active journey uses the mode).
    /// </summary>
    /// <param name="modeCode">Mode code to check for active journey usage.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if an active journey references the mode; false otherwise.</returns>
    private async Task<bool> ScanRedisJourneysForModeConflictAsync(string modeCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ScanRedisJourneysForModeConflictAsync");

        var journeyIndex = await _journeyIndexStore.GetAsync(JOURNEY_INDEX_KEY, cancellationToken);
        if (journeyIndex == null)
        {
            return false;
        }

        foreach (var journeyId in journeyIndex)
        {
            var journey = await _journeyStore.GetAsync(BuildJourneyKey(journeyId), cancellationToken: cancellationToken);
            if (journey == null) continue;
            if (journey.Status == JourneyStatus.Arrived || journey.Status == JourneyStatus.Abandoned) continue;

            if (journey.PrimaryModeCode == modeCode)
            {
                _logger.LogDebug("Found active Redis journey {JourneyId} using mode {ModeCode}", journeyId, modeCode);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scans all active Redis journeys and checks if any reference the given connection ID
    /// in their legs. Returns true if a conflict is found.
    /// </summary>
    /// <param name="connectionId">Connection ID to check for active journey usage.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if an active journey references the connection; false otherwise.</returns>
    private async Task<bool> ScanRedisJourneysForConnectionConflictAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ScanRedisJourneysForConnectionConflictAsync");

        var journeyIndex = await _journeyIndexStore.GetAsync(JOURNEY_INDEX_KEY, cancellationToken);
        if (journeyIndex == null)
        {
            return false;
        }

        foreach (var journeyId in journeyIndex)
        {
            var journey = await _journeyStore.GetAsync(BuildJourneyKey(journeyId), cancellationToken: cancellationToken);
            if (journey == null) continue;
            if (journey.Status == JourneyStatus.Arrived || journey.Status == JourneyStatus.Abandoned) continue;

            if (journey.Legs.Any(l => l.ConnectionId == connectionId))
            {
                _logger.LogDebug("Found active Redis journey {JourneyId} referencing connection {ConnectionId}", journeyId, connectionId);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scans Redis active journeys and interrupts any whose legs reference connections
    /// that touch the deleted location. Uses the already-queried affected connections list
    /// to build a set of connection IDs referencing the location.
    /// </summary>
    /// <param name="deletedLocationId">The deleted location ID.</param>
    /// <param name="affectedConnections">Connections already identified as referencing the deleted location.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of Redis journeys interrupted.</returns>
    private async Task<int> ScanRedisJourneysForLocationCleanupAsync(
        Guid deletedLocationId,
        IReadOnlyList<TransitConnectionModel> affectedConnections,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ScanRedisJourneysForLocationCleanupAsync");

        var journeyIndex = await _journeyIndexStore.GetAsync(JOURNEY_INDEX_KEY, cancellationToken);
        if (journeyIndex == null)
        {
            return 0;
        }

        // Build a set of connection IDs that reference the deleted location for efficient lookup
        var affectedConnectionIds = new HashSet<Guid>(affectedConnections.Select(c => c.Id));

        var interruptedCount = 0;

        foreach (var journeyId in journeyIndex)
        {
            var journey = await _journeyStore.GetAsync(BuildJourneyKey(journeyId), cancellationToken: cancellationToken);
            if (journey == null) continue;
            if (journey.Status == JourneyStatus.Arrived || journey.Status == JourneyStatus.Abandoned || journey.Status == JourneyStatus.Interrupted) continue;

            // Check if any leg references a connection touching the deleted location,
            // or if the journey's origin/destination is the deleted location
            var referencesLocation = journey.OriginLocationId == deletedLocationId ||
                                    journey.DestinationLocationId == deletedLocationId ||
                                    journey.Legs.Any(l => affectedConnectionIds.Contains(l.ConnectionId));

            if (!referencesLocation) continue;

            // Interrupt the journey in Redis
            journey.Status = JourneyStatus.Interrupted;
            journey.StatusReason = "location_deleted";
            journey.ModifiedAt = DateTimeOffset.UtcNow;

            var key = BuildJourneyKey(journeyId);
            await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

            // Publish transit.journey.interrupted event per FOUNDATION TENETS
            await PublishJourneyInterruptedEventAsync(journey, journey.RealmId, cancellationToken);

            // Publish client event
            await PublishJourneyUpdatedClientEventAsync(journey, cancellationToken);

            interruptedCount++;

            _logger.LogDebug("Interrupted active Redis journey {JourneyId} due to location {LocationId} deletion",
                journeyId, deletedLocationId);
        }

        if (interruptedCount > 0)
        {
            _logger.LogInformation("Interrupted {Count} active Redis journeys during location {LocationId} cleanup",
                interruptedCount, deletedLocationId);
        }

        return interruptedCount;
    }

    /// <summary>
    /// Scans Redis active journeys and abandons any belonging to the deleted entity.
    /// Character deletion permanently terminates journeys (Abandoned, not Interrupted).
    /// </summary>
    /// <param name="entityId">The deleted entity ID.</param>
    /// <param name="entityType">The entity type (e.g., "character").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of Redis journeys abandoned.</returns>
    private async Task<int> ScanRedisJourneysForCharacterCleanupAsync(
        Guid entityId,
        string entityType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ScanRedisJourneysForCharacterCleanupAsync");

        var journeyIndex = await _journeyIndexStore.GetAsync(JOURNEY_INDEX_KEY, cancellationToken);
        if (journeyIndex == null)
        {
            return 0;
        }

        var abandonedCount = 0;

        foreach (var journeyId in journeyIndex)
        {
            var journey = await _journeyStore.GetAsync(BuildJourneyKey(journeyId), cancellationToken: cancellationToken);
            if (journey == null) continue;
            if (journey.Status == JourneyStatus.Arrived || journey.Status == JourneyStatus.Abandoned) continue;

            if (journey.EntityId != entityId || journey.EntityType != entityType) continue;

            // Abandon the journey in Redis
            journey.Status = JourneyStatus.Abandoned;
            journey.StatusReason = "character_deleted";
            journey.ModifiedAt = DateTimeOffset.UtcNow;

            var key = BuildJourneyKey(journeyId);
            await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

            // Publish transit.journey.abandoned event per FOUNDATION TENETS
            await PublishJourneyAbandonedEventAsync(
                journey, journey.RealmId, journey.RealmId, journey.RealmId, cancellationToken);

            // Publish client event
            await PublishJourneyUpdatedClientEventAsync(journey, cancellationToken);

            abandonedCount++;

            _logger.LogDebug("Abandoned active Redis journey {JourneyId} due to character {EntityId} deletion",
                journeyId, entityId);
        }

        if (abandonedCount > 0)
        {
            _logger.LogInformation("Abandoned {Count} active Redis journeys during character {EntityId} cleanup",
                abandonedCount, entityId);
        }

        return abandonedCount;
    }

    #endregion
}
