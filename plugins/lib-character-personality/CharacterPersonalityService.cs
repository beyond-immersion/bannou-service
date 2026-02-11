using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.CharacterPersonality.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

// Note: InternalsVisibleTo is in AssemblyInfo.cs

namespace BeyondImmersion.BannouService.CharacterPersonality;

/// <summary>
/// Service implementation for character personality trait management.
/// Provides storage, retrieval, and evolution of personality traits and combat preferences.
/// </summary>
[BannouService("character-personality", typeof(ICharacterPersonalityService), lifetime: ServiceLifetime.Scoped)]
public partial class CharacterPersonalityService : ICharacterPersonalityService
{
    private readonly ILogger<CharacterPersonalityService> _logger;
    private readonly CharacterPersonalityServiceConfiguration _configuration;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly IPersonalityDataCache _personalityCache;

    private const string PERSONALITY_KEY_PREFIX = "personality-";
    private const string COMBAT_KEY_PREFIX = "combat-";

    // Event topics
    private const string PERSONALITY_CREATED_TOPIC = "personality.created";
    private const string PERSONALITY_UPDATED_TOPIC = "personality.updated";
    private const string PERSONALITY_EVOLVED_TOPIC = "personality.evolved";
    private const string PERSONALITY_DELETED_TOPIC = "personality.deleted";
    private const string COMBAT_PREFERENCES_CREATED_TOPIC = "combat-preferences.created";
    private const string COMBAT_PREFERENCES_UPDATED_TOPIC = "combat-preferences.updated";
    private const string COMBAT_PREFERENCES_EVOLVED_TOPIC = "combat-preferences.evolved";
    private const string COMBAT_PREFERENCES_DELETED_TOPIC = "combat-preferences.deleted";


    /// <summary>
    /// Initializes the CharacterPersonality service with required dependencies.
    /// </summary>
    public CharacterPersonalityService(
        ILogger<CharacterPersonalityService> logger,
        CharacterPersonalityServiceConfiguration configuration,
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IEventConsumer eventConsumer,
        IPersonalityDataCache personalityCache)
    {
        _logger = logger;
        _configuration = configuration;
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _personalityCache = personalityCache;

        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    // ============================================================================
    // Personality Trait Methods
    // ============================================================================

    /// <summary>
    /// Retrieves personality traits for a character.
    /// </summary>
    public async Task<(StatusCodes, PersonalityResponse?)> GetPersonalityAsync(GetPersonalityRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting personality for character {CharacterId}", body.CharacterId);

        {
            var store = _stateStoreFactory.GetStore<PersonalityData>(StateStoreDefinitions.CharacterPersonality);
            var data = await store.GetAsync($"{PERSONALITY_KEY_PREFIX}{body.CharacterId}", cancellationToken);

            if (data == null)
            {
                _logger.LogDebug("No personality found for character {CharacterId}", body.CharacterId);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToPersonalityResponse(data));
        }
    }

    /// <summary>
    /// Creates or updates personality traits for a character.
    /// </summary>
    public async Task<(StatusCodes, PersonalityResponse?)> SetPersonalityAsync(SetPersonalityRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Setting personality for character {CharacterId}", body.CharacterId);

        {
            var store = _stateStoreFactory.GetStore<PersonalityData>(StateStoreDefinitions.CharacterPersonality);
            var key = $"{PERSONALITY_KEY_PREFIX}{body.CharacterId}";
            var existing = await store.GetAsync(key, cancellationToken);
            var isNew = existing == null;

            var data = new PersonalityData
            {
                CharacterId = body.CharacterId,
                Traits = body.Traits.ToDictionary(t => t.Axis, t => t.Value),
                ArchetypeHint = body.ArchetypeHint,
                Version = isNew ? 1 : existing!.Version + 1,
                CreatedAtUnix = isNew ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : existing!.CreatedAtUnix,
                UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await store.SaveAsync(key, data, cancellationToken: cancellationToken);

            var response = MapToPersonalityResponse(data);

            // Publish event using typed events per IMPLEMENTATION TENETS
            if (isNew)
            {
                // Register character reference for new personality per IMPLEMENTATION TENETS
                await RegisterCharacterReferenceAsync(body.CharacterId.ToString(), body.CharacterId, cancellationToken);

                await _messageBus.TryPublishAsync(PERSONALITY_CREATED_TOPIC, new PersonalityCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    CharacterId = body.CharacterId,
                    Version = data.Version
                }, cancellationToken: cancellationToken);
            }
            else
            {
                await _messageBus.TryPublishAsync(PERSONALITY_UPDATED_TOPIC, new PersonalityUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    CharacterId = body.CharacterId,
                    Version = data.Version
                }, cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Personality {Action} for character {CharacterId}, version {Version}",
                isNew ? "created" : "updated", body.CharacterId, data.Version);

            return (StatusCodes.OK, response);
        }
    }

    /// <summary>
    /// Records an experience that may cause personality evolution.
    /// Evolution is probabilistic based on intensity and experience type.
    /// </summary>
    public async Task<(StatusCodes, ExperienceResult?)> RecordExperienceAsync(RecordExperienceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Recording experience for character {CharacterId}, type {ExperienceType}, intensity {Intensity}",
            body.CharacterId, body.ExperienceType, body.Intensity);

        var store = _stateStoreFactory.GetStore<PersonalityData>(StateStoreDefinitions.CharacterPersonality);
        var key = $"{PERSONALITY_KEY_PREFIX}{body.CharacterId}";
        var (data, etag) = await store.GetWithETagAsync(key, cancellationToken);

        if (data == null)
        {
            _logger.LogWarning("No personality found for character {CharacterId}", body.CharacterId);
            return (StatusCodes.NotFound, null);
        }

        // Evaluate whether evolution occurs (probability rolled once, not per-retry)
        var evolutionProbability = _configuration.BaseEvolutionProbability * body.Intensity;
        var roll = Random.Shared.NextDouble();
        var evolved = roll < evolutionProbability;

        var result = new ExperienceResult
        {
            CharacterId = body.CharacterId,
            ExperienceRecorded = true,
            PersonalityEvolved = evolved,
            ChangedTraits = new List<TraitValue>()
        };

        if (evolved)
        {
            var affectedTraits = GetAffectedTraits(body.ExperienceType);
            var saveSucceeded = false;

            // Optimistic concurrency retry loop: re-read fresh data on conflict
            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                if (attempt > 0)
                {
                    (data, etag) = await store.GetWithETagAsync(key, cancellationToken);
                    if (data == null)
                    {
                        _logger.LogWarning("Personality for character {CharacterId} deleted during evolution retry", body.CharacterId);
                        return (StatusCodes.NotFound, null);
                    }
                }

                // Apply trait shifts to fresh data each attempt
                var changedTraits = new List<TraitValue>();
                foreach (var (traitAxis, direction) in affectedTraits)
                {
                    if (data.Traits.TryGetValue(traitAxis, out var currentValue))
                    {
                        var shift = (float)((_configuration.MinTraitShift + (_configuration.MaxTraitShift - _configuration.MinTraitShift) * body.Intensity) * direction);
                        var newValue = Math.Clamp(currentValue + shift, -1.0f, 1.0f);
                        data.Traits[traitAxis] = newValue;

                        changedTraits.Add(new TraitValue
                        {
                            Axis = traitAxis,
                            Value = newValue
                        });
                    }
                }

                data.Version++;
                data.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var saveResult = await store.TrySaveAsync(key, data, etag ?? string.Empty, cancellationToken);
                if (saveResult != null)
                {
                    result.ChangedTraits = changedTraits;
                    result.NewVersion = data.Version;
                    saveSucceeded = true;

                    await _messageBus.TryPublishAsync(PERSONALITY_EVOLVED_TOPIC, new PersonalityEvolvedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        CharacterId = body.CharacterId,
                        ExperienceType = body.ExperienceType,
                        Intensity = body.Intensity,
                        Version = data.Version,
                        AffectedTraits = affectedTraits.Keys.Select(k => k.ToString()).ToList()
                    }, cancellationToken: cancellationToken);

                    _logger.LogInformation("Personality evolved for character {CharacterId}, new version {Version}",
                        body.CharacterId, data.Version);
                    break;
                }

                _logger.LogDebug("Concurrent modification during personality evolution for character {CharacterId}, retrying (attempt {Attempt})",
                    body.CharacterId, attempt + 1);
            }

            // If all retries exhausted without success, evolution did not persist
            if (!saveSucceeded)
            {
                _logger.LogWarning(
                    "Personality evolution for character {CharacterId} failed after {MaxRetries} retries due to concurrent modifications; evolution rolled but not persisted",
                    body.CharacterId, _configuration.MaxConcurrencyRetries);
                result.PersonalityEvolved = false;
            }
        }

        return (StatusCodes.OK, result);
    }

    /// <summary>
    /// Batch retrieves personalities for multiple characters.
    /// Used by behavior service for efficient region initialization.
    /// </summary>
    public async Task<(StatusCodes, BatchPersonalityResponse?)> BatchGetPersonalitiesAsync(BatchGetPersonalitiesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Batch getting personalities for {Count} characters", body.CharacterIds.Count);

        {
            if (body.CharacterIds.Count > _configuration.MaxBatchSize)
            {
                _logger.LogWarning("Batch get request exceeds maximum of {MaxBatchSize} characters", _configuration.MaxBatchSize);
                return (StatusCodes.BadRequest, null);
            }

            var store = _stateStoreFactory.GetStore<PersonalityData>(StateStoreDefinitions.CharacterPersonality);
            var personalities = new List<PersonalityResponse>();
            var notFound = new List<Guid>();

            foreach (var characterId in body.CharacterIds)
            {
                var data = await store.GetAsync($"{PERSONALITY_KEY_PREFIX}{characterId}", cancellationToken);
                if (data != null)
                {
                    personalities.Add(MapToPersonalityResponse(data));
                }
                else
                {
                    notFound.Add(characterId);
                }
            }

            var response = new BatchPersonalityResponse
            {
                Personalities = personalities,
                NotFound = notFound
            };

            _logger.LogInformation("Batch get returned {Found} personalities, {NotFound} not found",
                personalities.Count, notFound.Count);

            return (StatusCodes.OK, response);
        }
    }

    /// <summary>
    /// Deletes personality data for a character.
    /// </summary>
    public async Task<StatusCodes> DeletePersonalityAsync(DeletePersonalityRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting personality for character {CharacterId}", body.CharacterId);

        {
            var store = _stateStoreFactory.GetStore<PersonalityData>(StateStoreDefinitions.CharacterPersonality);
            var key = $"{PERSONALITY_KEY_PREFIX}{body.CharacterId}";
            var existing = await store.GetAsync(key, cancellationToken);

            if (existing == null)
            {
                return StatusCodes.NotFound;
            }

            // Unregister character reference before deletion per IMPLEMENTATION TENETS
            await UnregisterCharacterReferenceAsync(body.CharacterId.ToString(), body.CharacterId, cancellationToken);

            await store.DeleteAsync(key, cancellationToken);

            // Publish deletion event using typed events per IMPLEMENTATION TENETS
            await _messageBus.TryPublishAsync(PERSONALITY_DELETED_TOPIC, new PersonalityDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                CharacterId = body.CharacterId
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Personality deleted for character {CharacterId}", body.CharacterId);
            return StatusCodes.OK;
        }
    }

    // ============================================================================
    // Combat Preferences Methods
    // ============================================================================

    /// <summary>
    /// Retrieves combat preferences for a character.
    /// </summary>
    public async Task<(StatusCodes, CombatPreferencesResponse?)> GetCombatPreferencesAsync(GetCombatPreferencesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting combat preferences for character {CharacterId}", body.CharacterId);

        {
            var store = _stateStoreFactory.GetStore<CombatPreferencesData>(StateStoreDefinitions.CharacterPersonality);
            var data = await store.GetAsync($"{COMBAT_KEY_PREFIX}{body.CharacterId}", cancellationToken);

            if (data == null)
            {
                _logger.LogDebug("No combat preferences found for character {CharacterId}", body.CharacterId);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToCombatPreferencesResponse(data));
        }
    }

    /// <summary>
    /// Creates or updates combat preferences for a character.
    /// </summary>
    public async Task<(StatusCodes, CombatPreferencesResponse?)> SetCombatPreferencesAsync(SetCombatPreferencesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Setting combat preferences for character {CharacterId}", body.CharacterId);

        {
            var store = _stateStoreFactory.GetStore<CombatPreferencesData>(StateStoreDefinitions.CharacterPersonality);
            var key = $"{COMBAT_KEY_PREFIX}{body.CharacterId}";
            var existing = await store.GetAsync(key, cancellationToken);
            var isNew = existing == null;

            var data = new CombatPreferencesData
            {
                CharacterId = body.CharacterId,
                Style = body.Preferences.Style,
                PreferredRange = body.Preferences.PreferredRange,
                GroupRole = body.Preferences.GroupRole,
                RiskTolerance = body.Preferences.RiskTolerance,
                RetreatThreshold = body.Preferences.RetreatThreshold,
                ProtectAllies = body.Preferences.ProtectAllies,
                Version = isNew ? 1 : existing!.Version + 1,
                CreatedAtUnix = isNew ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : existing!.CreatedAtUnix,
                UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await store.SaveAsync(key, data, cancellationToken: cancellationToken);

            var response = MapToCombatPreferencesResponse(data);

            // Publish event using typed events per IMPLEMENTATION TENETS
            if (isNew)
            {
                // Register character reference for new combat preferences per IMPLEMENTATION TENETS
                await RegisterCharacterReferenceAsync($"combat-{body.CharacterId}", body.CharacterId, cancellationToken);

                await _messageBus.TryPublishAsync(COMBAT_PREFERENCES_CREATED_TOPIC, new CombatPreferencesCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    CharacterId = body.CharacterId,
                    Version = data.Version
                }, cancellationToken: cancellationToken);
            }
            else
            {
                await _messageBus.TryPublishAsync(COMBAT_PREFERENCES_UPDATED_TOPIC, new CombatPreferencesUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    CharacterId = body.CharacterId,
                    Version = data.Version
                }, cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Combat preferences {Action} for character {CharacterId}, version {Version}",
                isNew ? "created" : "updated", body.CharacterId, data.Version);

            return (StatusCodes.OK, response);
        }
    }

    /// <summary>
    /// Deletes combat preferences for a character.
    /// </summary>
    public async Task<StatusCodes> DeleteCombatPreferencesAsync(DeleteCombatPreferencesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting combat preferences for character {CharacterId}", body.CharacterId);

        {
            var store = _stateStoreFactory.GetStore<CombatPreferencesData>(StateStoreDefinitions.CharacterPersonality);
            var key = $"{COMBAT_KEY_PREFIX}{body.CharacterId}";
            var existing = await store.GetAsync(key, cancellationToken);

            if (existing == null)
            {
                return StatusCodes.NotFound;
            }

            // Unregister character reference before deletion per IMPLEMENTATION TENETS
            await UnregisterCharacterReferenceAsync($"combat-{body.CharacterId}", body.CharacterId, cancellationToken);

            await store.DeleteAsync(key, cancellationToken);

            // Publish deletion event using typed events per IMPLEMENTATION TENETS
            await _messageBus.TryPublishAsync(COMBAT_PREFERENCES_DELETED_TOPIC, new CombatPreferencesDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                CharacterId = body.CharacterId
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Combat preferences deleted for character {CharacterId}", body.CharacterId);
            return StatusCodes.OK;
        }
    }

    // ============================================================================
    // Resource Cleanup Methods
    // ============================================================================

    /// <summary>
    /// Cleans up all personality data (traits and combat preferences) for a deleted character.
    /// Called by lib-resource cleanup coordination during cascading resource cleanup.
    /// </summary>
    public async Task<(StatusCodes, CleanupByCharacterResponse?)> CleanupByCharacterAsync(
        CleanupByCharacterRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up all personality data for deleted character {CharacterId}", body.CharacterId);

        var personalityDeleted = false;
        var combatPreferencesDeleted = false;

        // Delete personality traits if they exist
        var personalityStore = _stateStoreFactory.GetStore<PersonalityData>(StateStoreDefinitions.CharacterPersonality);
        var personalityKey = $"{PERSONALITY_KEY_PREFIX}{body.CharacterId}";
        var existingPersonality = await personalityStore.GetAsync(personalityKey, cancellationToken);

        if (existingPersonality != null)
        {
            await personalityStore.DeleteAsync(personalityKey, cancellationToken);
            personalityDeleted = true;

            // Publish deletion event
            await _messageBus.TryPublishAsync(PERSONALITY_DELETED_TOPIC, new PersonalityDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                CharacterId = body.CharacterId
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Personality deleted for character {CharacterId} during cleanup", body.CharacterId);
        }

        // Delete combat preferences if they exist
        var combatStore = _stateStoreFactory.GetStore<CombatPreferencesData>(StateStoreDefinitions.CharacterPersonality);
        var combatKey = $"{COMBAT_KEY_PREFIX}{body.CharacterId}";
        var existingCombat = await combatStore.GetAsync(combatKey, cancellationToken);

        if (existingCombat != null)
        {
            await combatStore.DeleteAsync(combatKey, cancellationToken);
            combatPreferencesDeleted = true;

            // Publish deletion event
            await _messageBus.TryPublishAsync(COMBAT_PREFERENCES_DELETED_TOPIC, new CombatPreferencesDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                CharacterId = body.CharacterId
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Combat preferences deleted for character {CharacterId} during cleanup", body.CharacterId);
        }

        _logger.LogInformation(
            "Cleanup completed for character {CharacterId}: personality={PersonalityDeleted}, combat={CombatDeleted}",
            body.CharacterId, personalityDeleted, combatPreferencesDeleted);

        return (StatusCodes.OK, new CleanupByCharacterResponse
        {
            PersonalityDeleted = personalityDeleted,
            CombatPreferencesDeleted = combatPreferencesDeleted,
            Success = true
        });
    }

    /// <summary>
    /// Records a combat experience that may cause preference evolution.
    /// </summary>
    public async Task<(StatusCodes, CombatEvolutionResult?)> EvolveCombatPreferencesAsync(EvolveCombatRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Recording combat experience for character {CharacterId}, type {ExperienceType}, intensity {Intensity}",
            body.CharacterId, body.ExperienceType, body.Intensity);

        var store = _stateStoreFactory.GetStore<CombatPreferencesData>(StateStoreDefinitions.CharacterPersonality);
        var key = $"{COMBAT_KEY_PREFIX}{body.CharacterId}";
        var (data, etag) = await store.GetWithETagAsync(key, cancellationToken);

        if (data == null)
        {
            _logger.LogWarning("No combat preferences found for character {CharacterId}", body.CharacterId);
            return (StatusCodes.NotFound, null);
        }

        // Evaluate whether evolution occurs (probability rolled once, not per-retry)
        var evolutionProbability = _configuration.BaseEvolutionProbability * body.Intensity;
        var roll = Random.Shared.NextDouble();
        var evolved = roll < evolutionProbability;

        var result = new CombatEvolutionResult
        {
            CharacterId = body.CharacterId,
            ExperienceRecorded = true,
            PreferencesEvolved = evolved
        };

        if (evolved)
        {
            var saveSucceeded = false;

            // Optimistic concurrency retry loop: re-read fresh data on conflict
            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                if (attempt > 0)
                {
                    (data, etag) = await store.GetWithETagAsync(key, cancellationToken);
                    if (data == null)
                    {
                        _logger.LogWarning("Combat preferences for character {CharacterId} deleted during evolution retry", body.CharacterId);
                        return (StatusCodes.NotFound, null);
                    }
                }

                result.PreviousPreferences = MapToCombatPreferences(data);

                // Apply combat experience effects to fresh data each attempt
                ApplyCombatEvolution(data, body.ExperienceType, body.Intensity);

                data.Version++;
                data.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var saveResult = await store.TrySaveAsync(key, data, etag ?? string.Empty, cancellationToken);
                if (saveResult != null)
                {
                    result.NewPreferences = MapToCombatPreferences(data);
                    result.NewVersion = data.Version;
                    saveSucceeded = true;

                    await _messageBus.TryPublishAsync(COMBAT_PREFERENCES_EVOLVED_TOPIC, new CombatPreferencesEvolvedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        CharacterId = body.CharacterId,
                        ExperienceType = body.ExperienceType,
                        Intensity = body.Intensity,
                        Version = data.Version
                    }, cancellationToken: cancellationToken);

                    _logger.LogInformation("Combat preferences evolved for character {CharacterId}, new version {Version}",
                        body.CharacterId, data.Version);
                    break;
                }

                _logger.LogDebug("Concurrent modification during combat evolution for character {CharacterId}, retrying (attempt {Attempt})",
                    body.CharacterId, attempt + 1);
            }

            // If all retries exhausted without success, evolution did not persist
            if (!saveSucceeded)
            {
                _logger.LogWarning(
                    "Combat preferences evolution for character {CharacterId} failed after {MaxRetries} retries due to concurrent modifications; evolution rolled but not persisted",
                    body.CharacterId, _configuration.MaxConcurrencyRetries);
                result.PreferencesEvolved = false;
                result.PreviousPreferences = null;
            }
        }

        return (StatusCodes.OK, result);
    }

    // ============================================================================
    // Evolution Logic
    // ============================================================================

    /// <summary>
    /// Returns traits affected by an experience type and the direction of change.
    /// Positive = increase trait, Negative = decrease trait.
    /// </summary>
    private static Dictionary<TraitAxis, float> GetAffectedTraits(ExperienceType experienceType)
    {
        return experienceType switch
        {
            ExperienceType.TRAUMA => new Dictionary<TraitAxis, float>
            {
                { TraitAxis.NEUROTICISM, 0.5f },
                { TraitAxis.OPENNESS, -0.3f },
                { TraitAxis.EXTRAVERSION, -0.2f }
            },
            ExperienceType.BETRAYAL => new Dictionary<TraitAxis, float>
            {
                { TraitAxis.AGREEABLENESS, -0.5f },
                { TraitAxis.HONESTY, -0.3f },
                { TraitAxis.LOYALTY, 0.2f } // Can increase loyalty to those who remain
            },
            ExperienceType.LOSS => new Dictionary<TraitAxis, float>
            {
                { TraitAxis.NEUROTICISM, 0.3f },
                { TraitAxis.CONSCIENTIOUSNESS, 0.2f }
            },
            ExperienceType.VICTORY => new Dictionary<TraitAxis, float>
            {
                { TraitAxis.EXTRAVERSION, 0.3f },
                { TraitAxis.AGGRESSION, 0.2f },
                { TraitAxis.NEUROTICISM, -0.2f }
            },
            ExperienceType.FRIENDSHIP => new Dictionary<TraitAxis, float>
            {
                { TraitAxis.AGREEABLENESS, 0.4f },
                { TraitAxis.EXTRAVERSION, 0.3f },
                { TraitAxis.LOYALTY, 0.3f }
            },
            ExperienceType.REDEMPTION => new Dictionary<TraitAxis, float>
            {
                { TraitAxis.HONESTY, 0.4f },
                { TraitAxis.CONSCIENTIOUSNESS, 0.3f },
                { TraitAxis.NEUROTICISM, -0.2f }
            },
            ExperienceType.CORRUPTION => new Dictionary<TraitAxis, float>
            {
                { TraitAxis.HONESTY, -0.5f },
                { TraitAxis.AGREEABLENESS, -0.3f },
                { TraitAxis.AGGRESSION, 0.3f }
            },
            ExperienceType.ENLIGHTENMENT => new Dictionary<TraitAxis, float>
            {
                { TraitAxis.OPENNESS, 0.5f },
                { TraitAxis.CONSCIENTIOUSNESS, 0.3f },
                { TraitAxis.NEUROTICISM, -0.3f }
            },
            ExperienceType.SACRIFICE => new Dictionary<TraitAxis, float>
            {
                { TraitAxis.LOYALTY, 0.5f },
                { TraitAxis.CONSCIENTIOUSNESS, 0.3f },
                { TraitAxis.AGREEABLENESS, 0.2f }
            },
            _ => new Dictionary<TraitAxis, float>()
        };
    }

    /// <summary>
    /// Applies combat experience effects to preferences.
    /// </summary>
    private void ApplyCombatEvolution(CombatPreferencesData data, CombatExperienceType experienceType, float intensity)
    {
        var shift = (float)(_configuration.MinTraitShift + (_configuration.MaxTraitShift - _configuration.MinTraitShift) * intensity);

        switch (experienceType)
        {
            case CombatExperienceType.DECISIVE_VICTORY:
                data.RiskTolerance = Math.Clamp(data.RiskTolerance + shift, 0, 1);
                // May become more aggressive
                if (data.Style == CombatStyle.DEFENSIVE && Random.Shared.NextDouble() < _configuration.CombatStyleTransitionProbability)
                    data.Style = CombatStyle.BALANCED;
                else if (data.Style == CombatStyle.BALANCED && Random.Shared.NextDouble() < _configuration.CombatVictoryBalancedTransitionProbability)
                    data.Style = CombatStyle.AGGRESSIVE;
                break;

            case CombatExperienceType.NARROW_VICTORY:
                // Slight confidence boost but also caution
                data.RiskTolerance = Math.Clamp(data.RiskTolerance + shift * (float)_configuration.CombatMildShiftMultiplier, 0, 1);
                break;

            case CombatExperienceType.DEFEAT:
                data.RiskTolerance = Math.Clamp(data.RiskTolerance - shift, 0, 1);
                data.RetreatThreshold = Math.Clamp(data.RetreatThreshold + shift * (float)_configuration.CombatMildShiftMultiplier, 0, 1);
                // May become more defensive
                if (data.Style == CombatStyle.AGGRESSIVE && Random.Shared.NextDouble() < _configuration.CombatStyleTransitionProbability)
                    data.Style = CombatStyle.BALANCED;
                else if (data.Style == CombatStyle.BERSERKER && Random.Shared.NextDouble() < _configuration.CombatDefeatStyleTransitionProbability)
                    data.Style = CombatStyle.AGGRESSIVE;
                break;

            case CombatExperienceType.NEAR_DEATH:
                data.RetreatThreshold = Math.Clamp(data.RetreatThreshold + shift * (float)_configuration.CombatIntenseShiftMultiplier, 0, 1);
                data.RiskTolerance = Math.Clamp(data.RiskTolerance - shift * (float)_configuration.CombatIntenseShiftMultiplier, 0, 1);
                // High chance of becoming more defensive
                if (data.Style != CombatStyle.DEFENSIVE && Random.Shared.NextDouble() < _configuration.CombatDefensiveShiftProbability)
                    data.Style = CombatStyle.DEFENSIVE;
                break;

            case CombatExperienceType.ALLY_SAVED:
                data.ProtectAllies = true;
                if (data.GroupRole == GroupRole.SOLO && Random.Shared.NextDouble() < _configuration.CombatRoleTransitionProbability)
                    data.GroupRole = GroupRole.SUPPORT;
                break;

            case CombatExperienceType.ALLY_LOST:
                // Complex - may increase or decrease protection tendency
                if (Random.Shared.NextDouble() < _configuration.CombatDefensiveShiftProbability)
                {
                    data.ProtectAllies = true; // Determined to not let it happen again
                }
                else
                {
                    data.ProtectAllies = false; // Self-preservation kicks in
                }
                break;

            case CombatExperienceType.SUCCESSFUL_RETREAT:
                // Validates retreat as a viable strategy
                data.RetreatThreshold = Math.Clamp(data.RetreatThreshold + shift * (float)_configuration.CombatMildestShiftMultiplier, 0, 1);
                break;

            case CombatExperienceType.FAILED_RETREAT:
                // May fight harder next time or become more cautious
                if (Random.Shared.NextDouble() < _configuration.CombatDefensiveShiftProbability)
                {
                    data.RetreatThreshold = Math.Clamp(data.RetreatThreshold - shift, 0, 1);
                    data.Style = CombatStyle.AGGRESSIVE; // Fight instead of flee
                }
                else
                {
                    data.RetreatThreshold = Math.Clamp(data.RetreatThreshold + shift, 0, 1);
                }
                break;

            case CombatExperienceType.AMBUSH_SUCCESS:
                if (data.GroupRole != GroupRole.LEADER && Random.Shared.NextDouble() < _configuration.CombatStyleTransitionProbability)
                    data.GroupRole = GroupRole.FLANKER;
                data.Style = data.Style == CombatStyle.DEFENSIVE ? CombatStyle.TACTICAL : data.Style;
                break;

            case CombatExperienceType.AMBUSH_SURVIVED:
                // More paranoid, more defensive
                data.RiskTolerance = Math.Clamp(data.RiskTolerance - shift, 0, 1);
                break;
        }
    }

    // ============================================================================
    // Mapping Helpers
    // ============================================================================

    private static PersonalityResponse MapToPersonalityResponse(PersonalityData data)
    {
        return new PersonalityResponse
        {
            CharacterId = data.CharacterId,
            Traits = data.Traits.Select(t => new TraitValue
            {
                Axis = t.Key,
                Value = t.Value
            }).ToList(),
            ArchetypeHint = data.ArchetypeHint,
            Version = data.Version,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix),
            UpdatedAt = data.UpdatedAtUnix != data.CreatedAtUnix
                ? DateTimeOffset.FromUnixTimeSeconds(data.UpdatedAtUnix)
                : null
        };
    }

    private static CombatPreferencesResponse MapToCombatPreferencesResponse(CombatPreferencesData data)
    {
        return new CombatPreferencesResponse
        {
            CharacterId = data.CharacterId,
            Preferences = MapToCombatPreferences(data),
            Version = data.Version,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix),
            UpdatedAt = data.UpdatedAtUnix != data.CreatedAtUnix
                ? DateTimeOffset.FromUnixTimeSeconds(data.UpdatedAtUnix)
                : null
        };
    }

    private static CombatPreferences MapToCombatPreferences(CombatPreferencesData data)
    {
        return new CombatPreferences
        {
            Style = data.Style,
            PreferredRange = data.PreferredRange,
            GroupRole = data.GroupRole,
            RiskTolerance = data.RiskTolerance,
            RetreatThreshold = data.RetreatThreshold,
            ProtectAllies = data.ProtectAllies
        };
    }

    // ============================================================================
    // Compression Methods
    // ============================================================================

    /// <summary>
    /// Gets personality data for compression during character archival.
    /// Called by Resource service during character compression via compression callback.
    /// </summary>
    public async Task<(StatusCodes, CharacterPersonalityArchive?)> GetCompressDataAsync(
        GetCompressDataRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting compress data for character {CharacterId}", body.CharacterId);

        {
            var personalityStore = _stateStoreFactory.GetStore<PersonalityData>(StateStoreDefinitions.CharacterPersonality);
            var combatStore = _stateStoreFactory.GetStore<CombatPreferencesData>(StateStoreDefinitions.CharacterPersonality);

            var personalityData = await personalityStore.GetAsync($"{PERSONALITY_KEY_PREFIX}{body.CharacterId}", cancellationToken);
            var combatData = await combatStore.GetAsync($"{COMBAT_KEY_PREFIX}{body.CharacterId}", cancellationToken);

            // Return 404 only if BOTH are missing
            if (personalityData == null && combatData == null)
            {
                _logger.LogDebug("No personality data found for character {CharacterId}", body.CharacterId);
                return (StatusCodes.NotFound, null);
            }

            var response = new CharacterPersonalityArchive
            {
                // ResourceArchiveBase fields
                ResourceId = body.CharacterId,
                ResourceType = "character-personality",
                ArchivedAt = DateTimeOffset.UtcNow,
                SchemaVersion = 1,
                // Service-specific fields
                CharacterId = body.CharacterId,
                HasPersonality = personalityData != null,
                Personality = personalityData != null ? MapToPersonalityResponse(personalityData) : null,
                HasCombatPreferences = combatData != null,
                CombatPreferences = combatData != null ? MapToCombatPreferencesResponse(combatData) : null
            };

            _logger.LogInformation(
                "Compress data retrieved for character {CharacterId}: personality={HasPersonality}, combat={HasCombat}",
                body.CharacterId, response.HasPersonality, response.HasCombatPreferences);

            return (StatusCodes.OK, response);
        }
    }

    /// <summary>
    /// Restores personality data from a compressed archive.
    /// Called by Resource service during character decompression via decompression callback.
    /// </summary>
    public async Task<(StatusCodes, RestoreFromArchiveResponse?)> RestoreFromArchiveAsync(
        RestoreFromArchiveRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restoring personality data from archive for character {CharacterId}", body.CharacterId);

        var personalityRestored = false;
        var combatPreferencesRestored = false;

        // Decompress the archive data
        CharacterPersonalityArchive archiveData;
        try
        {
            var compressedBytes = Convert.FromBase64String(body.Data);
            var jsonData = DecompressJsonData(compressedBytes);
            archiveData = BannouJson.Deserialize<CharacterPersonalityArchive>(jsonData)
                ?? throw new InvalidOperationException("Deserialized archive data is null");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decompress archive data for character {CharacterId}", body.CharacterId);
            return (StatusCodes.BadRequest, new RestoreFromArchiveResponse
            {
                CharacterId = body.CharacterId,
                PersonalityRestored = false,
                CombatPreferencesRestored = false,
                Success = false,
                ErrorMessage = $"Invalid archive data: {ex.Message}"
            });
        }

        // Restore personality traits if present in archive
        if (archiveData.HasPersonality && archiveData.Personality != null)
        {
            var personalityStore = _stateStoreFactory.GetStore<PersonalityData>(StateStoreDefinitions.CharacterPersonality);
            var personalityKey = $"{PERSONALITY_KEY_PREFIX}{body.CharacterId}";

            var data = new PersonalityData
            {
                CharacterId = body.CharacterId,
                Traits = archiveData.Personality.Traits.ToDictionary(t => t.Axis, t => t.Value),
                ArchetypeHint = archiveData.Personality.ArchetypeHint,
                Version = archiveData.Personality.Version + 1, // Increment version for restored data
                CreatedAtUnix = archiveData.Personality.CreatedAt.ToUnixTimeSeconds(),
                UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await personalityStore.SaveAsync(personalityKey, data, cancellationToken: cancellationToken);
            personalityRestored = true;

            // Register character reference for restored personality
            await RegisterCharacterReferenceAsync(body.CharacterId.ToString(), body.CharacterId, cancellationToken);

            _logger.LogInformation("Personality traits restored for character {CharacterId}", body.CharacterId);
        }

        // Restore combat preferences if present in archive
        if (archiveData.HasCombatPreferences && archiveData.CombatPreferences != null)
        {
            var combatStore = _stateStoreFactory.GetStore<CombatPreferencesData>(StateStoreDefinitions.CharacterPersonality);
            var combatKey = $"{COMBAT_KEY_PREFIX}{body.CharacterId}";

            var data = new CombatPreferencesData
            {
                CharacterId = body.CharacterId,
                Style = archiveData.CombatPreferences.Preferences.Style,
                PreferredRange = archiveData.CombatPreferences.Preferences.PreferredRange,
                GroupRole = archiveData.CombatPreferences.Preferences.GroupRole,
                RiskTolerance = archiveData.CombatPreferences.Preferences.RiskTolerance,
                RetreatThreshold = archiveData.CombatPreferences.Preferences.RetreatThreshold,
                ProtectAllies = archiveData.CombatPreferences.Preferences.ProtectAllies,
                Version = archiveData.CombatPreferences.Version + 1, // Increment version for restored data
                CreatedAtUnix = archiveData.CombatPreferences.CreatedAt.ToUnixTimeSeconds(),
                UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await combatStore.SaveAsync(combatKey, data, cancellationToken: cancellationToken);
            combatPreferencesRestored = true;

            // Register character reference for restored combat preferences
            await RegisterCharacterReferenceAsync($"combat-{body.CharacterId}", body.CharacterId, cancellationToken);

            _logger.LogInformation("Combat preferences restored for character {CharacterId}", body.CharacterId);
        }

        _logger.LogInformation(
            "Archive restoration completed for character {CharacterId}: personality={PersonalityRestored}, combat={CombatRestored}",
            body.CharacterId, personalityRestored, combatPreferencesRestored);

        return (StatusCodes.OK, new RestoreFromArchiveResponse
        {
            CharacterId = body.CharacterId,
            PersonalityRestored = personalityRestored,
            CombatPreferencesRestored = combatPreferencesRestored,
            Success = true
        });
    }

    /// <summary>
    /// Decompresses gzipped JSON data.
    /// </summary>
    private static string DecompressJsonData(byte[] compressedData)
    {
        using var input = new System.IO.MemoryStream(compressedData);
        using var gzip = new System.IO.Compression.GZipStream(
            input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new System.IO.MemoryStream();
        gzip.CopyTo(output);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    // ============================================================================
    // Permission Registration
    // ============================================================================

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering CharacterPersonality service permissions...");
        try
        {
            await CharacterPersonalityPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
            _logger.LogInformation("CharacterPersonality service permissions registered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register CharacterPersonality service permissions");
            await _messageBus.TryPublishErrorAsync(
                "character-personality",
                "RegisterServicePermissions",
                ex.GetType().Name,
                ex.Message,
                dependency: "permission");
            throw;
        }
    }
}
