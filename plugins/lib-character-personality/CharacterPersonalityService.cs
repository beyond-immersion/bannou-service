using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-character-personality.tests")]

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
        IEventConsumer eventConsumer)
    {
        _logger = logger;
        _configuration = configuration;
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;

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
        _logger.LogInformation("Getting personality for character {CharacterId}", body.CharacterId);

        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting personality for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-personality",
                "GetPersonality",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-personality/get",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Creates or updates personality traits for a character.
    /// </summary>
    public async Task<(StatusCodes, PersonalityResponse?)> SetPersonalityAsync(SetPersonalityRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Setting personality for character {CharacterId}", body.CharacterId);

        try
        {
            var store = _stateStoreFactory.GetStore<PersonalityData>(StateStoreDefinitions.CharacterPersonality);
            var key = $"{PERSONALITY_KEY_PREFIX}{body.CharacterId}";
            var existing = await store.GetAsync(key, cancellationToken);
            var isNew = existing == null;

            var data = new PersonalityData
            {
                CharacterId = body.CharacterId.ToString(),
                Traits = body.Traits.ToDictionary(t => t.Axis.ToString(), t => t.Value),
                Version = isNew ? 1 : existing!.Version + 1,
                CreatedAtUnix = isNew ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : existing!.CreatedAtUnix,
                UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await store.SaveAsync(key, data, cancellationToken: cancellationToken);

            var response = MapToPersonalityResponse(data);

            // Publish event using typed events per IMPLEMENTATION TENETS (TENET 5)
            if (isNew)
            {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting personality for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-personality",
                "SetPersonality",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-personality/set",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Records an experience that may cause personality evolution.
    /// Evolution is probabilistic based on intensity and experience type.
    /// </summary>
    public async Task<(StatusCodes, ExperienceResult?)> RecordExperienceAsync(RecordExperienceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Recording experience for character {CharacterId}, type {ExperienceType}, intensity {Intensity}",
            body.CharacterId, body.ExperienceType, body.Intensity);

        try
        {
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

                // Optimistic concurrency retry loop: re-read fresh data on conflict
                for (var attempt = 0; attempt < 3; attempt++)
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
                    foreach (var (trait, direction) in affectedTraits)
                    {
                        if (data.Traits.TryGetValue(trait, out var currentValue))
                        {
                            var shift = (float)((_configuration.MinTraitShift + (_configuration.MaxTraitShift - _configuration.MinTraitShift) * body.Intensity) * direction);
                            var newValue = Math.Clamp(currentValue + shift, -1.0f, 1.0f);
                            data.Traits[trait] = newValue;

                            changedTraits.Add(new TraitValue
                            {
                                Axis = Enum.Parse<TraitAxis>(trait),
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

                        await _messageBus.TryPublishAsync(PERSONALITY_EVOLVED_TOPIC, new PersonalityEvolvedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            CharacterId = body.CharacterId,
                            ExperienceType = body.ExperienceType.ToString(),
                            Intensity = body.Intensity,
                            Version = data.Version,
                            AffectedTraits = affectedTraits.Keys.ToList()
                        }, cancellationToken: cancellationToken);

                        _logger.LogInformation("Personality evolved for character {CharacterId}, new version {Version}",
                            body.CharacterId, data.Version);
                        break;
                    }

                    _logger.LogDebug("Concurrent modification during personality evolution for character {CharacterId}, retrying (attempt {Attempt})",
                        body.CharacterId, attempt + 1);
                }
            }

            return (StatusCodes.OK, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording experience for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-personality",
                "RecordExperience",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-personality/evolve",
                details: new { body.CharacterId, body.ExperienceType },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Batch retrieves personalities for multiple characters.
    /// Used by behavior service for efficient region initialization.
    /// </summary>
    public async Task<(StatusCodes, BatchPersonalityResponse?)> BatchGetPersonalitiesAsync(BatchGetPersonalitiesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Batch getting personalities for {Count} characters", body.CharacterIds.Count);

        try
        {
            if (body.CharacterIds.Count > 100)
            {
                _logger.LogWarning("Batch get request exceeds maximum of 100 characters");
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error batch getting personalities");
            await _messageBus.TryPublishErrorAsync(
                "character-personality",
                "BatchGetPersonalities",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-personality/batch-get",
                details: new { Count = body.CharacterIds.Count },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes personality data for a character.
    /// </summary>
    public async Task<StatusCodes> DeletePersonalityAsync(DeletePersonalityRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting personality for character {CharacterId}", body.CharacterId);

        try
        {
            var store = _stateStoreFactory.GetStore<PersonalityData>(StateStoreDefinitions.CharacterPersonality);
            var key = $"{PERSONALITY_KEY_PREFIX}{body.CharacterId}";
            var existing = await store.GetAsync(key, cancellationToken);

            if (existing == null)
            {
                return StatusCodes.NotFound;
            }

            await store.DeleteAsync(key, cancellationToken);

            // Publish deletion event using typed events per IMPLEMENTATION TENETS (TENET 5)
            await _messageBus.TryPublishAsync(PERSONALITY_DELETED_TOPIC, new PersonalityDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                CharacterId = body.CharacterId
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Personality deleted for character {CharacterId}", body.CharacterId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting personality for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-personality",
                "DeletePersonality",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-personality/delete",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
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
        _logger.LogInformation("Getting combat preferences for character {CharacterId}", body.CharacterId);

        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting combat preferences for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-personality",
                "GetCombatPreferences",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-personality/get-combat",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Creates or updates combat preferences for a character.
    /// </summary>
    public async Task<(StatusCodes, CombatPreferencesResponse?)> SetCombatPreferencesAsync(SetCombatPreferencesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Setting combat preferences for character {CharacterId}", body.CharacterId);

        try
        {
            var store = _stateStoreFactory.GetStore<CombatPreferencesData>(StateStoreDefinitions.CharacterPersonality);
            var key = $"{COMBAT_KEY_PREFIX}{body.CharacterId}";
            var existing = await store.GetAsync(key, cancellationToken);
            var isNew = existing == null;

            var data = new CombatPreferencesData
            {
                CharacterId = body.CharacterId.ToString(),
                Style = body.Preferences.Style.ToString(),
                PreferredRange = body.Preferences.PreferredRange.ToString(),
                GroupRole = body.Preferences.GroupRole.ToString(),
                RiskTolerance = body.Preferences.RiskTolerance,
                RetreatThreshold = body.Preferences.RetreatThreshold,
                ProtectAllies = body.Preferences.ProtectAllies,
                Version = isNew ? 1 : existing!.Version + 1,
                CreatedAtUnix = isNew ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : existing!.CreatedAtUnix,
                UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await store.SaveAsync(key, data, cancellationToken: cancellationToken);

            var response = MapToCombatPreferencesResponse(data);

            // Publish event using typed events per IMPLEMENTATION TENETS (TENET 5)
            if (isNew)
            {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting combat preferences for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-personality",
                "SetCombatPreferences",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-personality/set-combat",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes combat preferences for a character.
    /// </summary>
    public async Task<StatusCodes> DeleteCombatPreferencesAsync(DeleteCombatPreferencesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting combat preferences for character {CharacterId}", body.CharacterId);

        try
        {
            var store = _stateStoreFactory.GetStore<CombatPreferencesData>(StateStoreDefinitions.CharacterPersonality);
            var key = $"{COMBAT_KEY_PREFIX}{body.CharacterId}";
            var existing = await store.GetAsync(key, cancellationToken);

            if (existing == null)
            {
                return StatusCodes.NotFound;
            }

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting combat preferences for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-personality",
                "DeleteCombatPreferences",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-personality/delete-combat",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Records a combat experience that may cause preference evolution.
    /// </summary>
    public async Task<(StatusCodes, CombatEvolutionResult?)> EvolveCombatPreferencesAsync(EvolveCombatRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Recording combat experience for character {CharacterId}, type {ExperienceType}, intensity {Intensity}",
            body.CharacterId, body.ExperienceType, body.Intensity);

        try
        {
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
                // Optimistic concurrency retry loop: re-read fresh data on conflict
                for (var attempt = 0; attempt < 3; attempt++)
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

                        await _messageBus.TryPublishAsync(COMBAT_PREFERENCES_EVOLVED_TOPIC, new CombatPreferencesEvolvedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            CharacterId = body.CharacterId,
                            ExperienceType = body.ExperienceType.ToString(),
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
            }

            return (StatusCodes.OK, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording combat experience for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-personality",
                "EvolveCombatPreferences",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-personality/evolve-combat",
                details: new { body.CharacterId, body.ExperienceType },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // ============================================================================
    // Evolution Logic
    // ============================================================================

    /// <summary>
    /// Returns traits affected by an experience type and the direction of change.
    /// Positive = increase trait, Negative = decrease trait.
    /// </summary>
    private static Dictionary<string, float> GetAffectedTraits(ExperienceType experienceType)
    {
        return experienceType switch
        {
            ExperienceType.TRAUMA => new Dictionary<string, float>
            {
                { "NEUROTICISM", 0.5f },
                { "OPENNESS", -0.3f },
                { "EXTRAVERSION", -0.2f }
            },
            ExperienceType.BETRAYAL => new Dictionary<string, float>
            {
                { "AGREEABLENESS", -0.5f },
                { "HONESTY", -0.3f },
                { "LOYALTY", 0.2f } // Can increase loyalty to those who remain
            },
            ExperienceType.LOSS => new Dictionary<string, float>
            {
                { "NEUROTICISM", 0.3f },
                { "CONSCIENTIOUSNESS", 0.2f }
            },
            ExperienceType.VICTORY => new Dictionary<string, float>
            {
                { "EXTRAVERSION", 0.3f },
                { "AGGRESSION", 0.2f },
                { "NEUROTICISM", -0.2f }
            },
            ExperienceType.FRIENDSHIP => new Dictionary<string, float>
            {
                { "AGREEABLENESS", 0.4f },
                { "EXTRAVERSION", 0.3f },
                { "LOYALTY", 0.3f }
            },
            ExperienceType.REDEMPTION => new Dictionary<string, float>
            {
                { "HONESTY", 0.4f },
                { "CONSCIENTIOUSNESS", 0.3f },
                { "NEUROTICISM", -0.2f }
            },
            ExperienceType.CORRUPTION => new Dictionary<string, float>
            {
                { "HONESTY", -0.5f },
                { "AGREEABLENESS", -0.3f },
                { "AGGRESSION", 0.3f }
            },
            ExperienceType.ENLIGHTENMENT => new Dictionary<string, float>
            {
                { "OPENNESS", 0.5f },
                { "CONSCIENTIOUSNESS", 0.3f },
                { "NEUROTICISM", -0.3f }
            },
            ExperienceType.SACRIFICE => new Dictionary<string, float>
            {
                { "LOYALTY", 0.5f },
                { "CONSCIENTIOUSNESS", 0.3f },
                { "AGREEABLENESS", 0.2f }
            },
            _ => new Dictionary<string, float>()
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
                if (data.Style == "DEFENSIVE" && Random.Shared.NextDouble() < 0.3)
                    data.Style = "BALANCED";
                else if (data.Style == "BALANCED" && Random.Shared.NextDouble() < 0.2)
                    data.Style = "AGGRESSIVE";
                break;

            case CombatExperienceType.NARROW_VICTORY:
                // Slight confidence boost but also caution
                data.RiskTolerance = Math.Clamp(data.RiskTolerance + shift * 0.5f, 0, 1);
                break;

            case CombatExperienceType.DEFEAT:
                data.RiskTolerance = Math.Clamp(data.RiskTolerance - shift, 0, 1);
                data.RetreatThreshold = Math.Clamp(data.RetreatThreshold + shift * 0.5f, 0, 1);
                // May become more defensive
                if (data.Style == "AGGRESSIVE" && Random.Shared.NextDouble() < 0.3)
                    data.Style = "BALANCED";
                else if (data.Style == "BERSERKER" && Random.Shared.NextDouble() < 0.4)
                    data.Style = "AGGRESSIVE";
                break;

            case CombatExperienceType.NEAR_DEATH:
                data.RetreatThreshold = Math.Clamp(data.RetreatThreshold + shift * 1.5f, 0, 1);
                data.RiskTolerance = Math.Clamp(data.RiskTolerance - shift * 1.5f, 0, 1);
                // High chance of becoming more defensive
                if (data.Style != "DEFENSIVE" && Random.Shared.NextDouble() < 0.5)
                    data.Style = "DEFENSIVE";
                break;

            case CombatExperienceType.ALLY_SAVED:
                data.ProtectAllies = true;
                if (data.GroupRole == "SOLO" && Random.Shared.NextDouble() < 0.4)
                    data.GroupRole = "SUPPORT";
                break;

            case CombatExperienceType.ALLY_LOST:
                // Complex - may increase or decrease protection tendency
                if (Random.Shared.NextDouble() < 0.5)
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
                data.RetreatThreshold = Math.Clamp(data.RetreatThreshold + shift * 0.3f, 0, 1);
                break;

            case CombatExperienceType.FAILED_RETREAT:
                // May fight harder next time or become more cautious
                if (Random.Shared.NextDouble() < 0.5)
                {
                    data.RetreatThreshold = Math.Clamp(data.RetreatThreshold - shift, 0, 1);
                    data.Style = "AGGRESSIVE"; // Fight instead of flee
                }
                else
                {
                    data.RetreatThreshold = Math.Clamp(data.RetreatThreshold + shift, 0, 1);
                }
                break;

            case CombatExperienceType.AMBUSH_SUCCESS:
                if (data.GroupRole != "LEADER" && Random.Shared.NextDouble() < 0.3)
                    data.GroupRole = "FLANKER";
                data.Style = data.Style == "DEFENSIVE" ? "TACTICAL" : data.Style;
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
            CharacterId = Guid.Parse(data.CharacterId),
            Traits = data.Traits.Select(t => new TraitValue
            {
                Axis = Enum.Parse<TraitAxis>(t.Key),
                Value = t.Value
            }).ToList(),
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
            CharacterId = Guid.Parse(data.CharacterId),
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
            Style = Enum.Parse<CombatStyle>(data.Style),
            PreferredRange = Enum.Parse<PreferredRange>(data.PreferredRange),
            GroupRole = Enum.Parse<GroupRole>(data.GroupRole),
            RiskTolerance = data.RiskTolerance,
            RetreatThreshold = data.RetreatThreshold,
            ProtectAllies = data.ProtectAllies
        };
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

// ============================================================================
// Internal Data Models
// ============================================================================

/// <summary>
/// Internal storage model for personality data.
/// </summary>
internal class PersonalityData
{
    public string CharacterId { get; set; } = string.Empty;
    public Dictionary<string, float> Traits { get; set; } = new();
    public int Version { get; set; }
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
}

/// <summary>
/// Internal storage model for combat preferences data.
/// </summary>
internal class CombatPreferencesData
{
    public string CharacterId { get; set; } = string.Empty;
    public string Style { get; set; } = "BALANCED";
    public string PreferredRange { get; set; } = "MEDIUM";
    public string GroupRole { get; set; } = "FRONTLINE";
    public float RiskTolerance { get; set; } = 0.5f;
    public float RetreatThreshold { get; set; } = 0.3f;
    public bool ProtectAllies { get; set; } = true;
    public int Version { get; set; }
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
}
