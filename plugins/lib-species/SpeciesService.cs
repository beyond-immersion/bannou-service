using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Species;

/// <summary>
/// Implementation of the Species service.
/// Manages species definitions for characters in game worlds.
/// Species are realm-specific, allowing different realms to have distinct populations.
/// </summary>
[BannouService("species", typeof(ISpeciesService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class SpeciesService : ISpeciesService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<SpeciesService> _logger;
    private readonly SpeciesServiceConfiguration _configuration;
    private readonly ICharacterClient _characterClient;
    private readonly IRealmClient _realmClient;

    private const string SPECIES_KEY_PREFIX = "species:";
    private const string CODE_INDEX_PREFIX = "code-index:";
    private const string REALM_INDEX_PREFIX = "realm-index:";
    private const string ALL_SPECIES_KEY = "all-species";

    public SpeciesService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<SpeciesService> logger,
        SpeciesServiceConfiguration configuration,
        ICharacterClient characterClient,
        IRealmClient realmClient,
        IEventConsumer eventConsumer)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _characterClient = characterClient;
        _realmClient = realmClient;

        // Register event handlers via partial class (SpeciesServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    #region Key Building Helpers

    private static string BuildSpeciesKey(Guid speciesId) => $"{SPECIES_KEY_PREFIX}{speciesId}";
    private static string BuildCodeIndexKey(string code) => $"{CODE_INDEX_PREFIX}{code.ToUpperInvariant()}";
    private static string BuildRealmIndexKey(Guid realmId) => $"{REALM_INDEX_PREFIX}{realmId}";

    #endregion

    #region Realm Validation Helpers

    /// <summary>
    /// Validates that a realm exists and is active (not deprecated).
    /// </summary>
    private async Task<(bool exists, bool isActive)> ValidateRealmAsync(Guid realmId, CancellationToken cancellationToken)
    {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not validate realm {RealmId} - failing operation (fail closed)", realmId);
            // If RealmService is unavailable, fail the operation - don't assume realm is valid
            throw new InvalidOperationException($"Cannot validate realm {realmId}: RealmService unavailable", ex);
        }
    }

    /// <summary>
    /// Validates multiple realms in parallel and returns lists of invalid and deprecated realm IDs.
    /// </summary>
    private async Task<(List<Guid> invalidRealms, List<Guid> deprecatedRealms)> ValidateRealmsAsync(
        IEnumerable<Guid> realmIds,
        CancellationToken cancellationToken)
    {
        var realmIdList = realmIds.ToList();
        var validationTasks = realmIdList.Select(async realmId =>
        {
            var (exists, isActive) = await ValidateRealmAsync(realmId, cancellationToken);
            return (realmId, exists, isActive);
        });

        var results = await Task.WhenAll(validationTasks);

        var invalidRealms = new List<Guid>();
        var deprecatedRealms = new List<Guid>();

        foreach (var (realmId, exists, isActive) in results)
        {
            if (!exists)
            {
                invalidRealms.Add(realmId);
            }
            else if (!isActive)
            {
                deprecatedRealms.Add(realmId);
            }
        }

        return (invalidRealms, deprecatedRealms);
    }

    #endregion

    #region Read Operations

    public async Task<(StatusCodes, SpeciesResponse?)> GetSpeciesAsync(
        GetSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting species by ID: {SpeciesId}", body.SpeciesId);

        var speciesKey = BuildSpeciesKey(body.SpeciesId);
        var model = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetAsync(speciesKey, cancellationToken: cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Species not found: {SpeciesId}", body.SpeciesId);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapToResponse(model));
    }

    public async Task<(StatusCodes, SpeciesResponse?)> GetSpeciesByCodeAsync(
        GetSpeciesByCodeRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting species by code: {Code}", body.Code);

        var codeIndexKey = BuildCodeIndexKey(body.Code);
        var speciesIdStr = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Species).GetAsync(codeIndexKey, cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(speciesIdStr) || !Guid.TryParse(speciesIdStr, out var speciesId))
        {
            _logger.LogDebug("Species not found by code: {Code}", body.Code);
            return (StatusCodes.NotFound, null);
        }

        var speciesKey = BuildSpeciesKey(speciesId);
        var model = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetAsync(speciesKey, cancellationToken: cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Species data inconsistency - code index exists but species not found: {Code}", body.Code);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapToResponse(model));
    }

    public async Task<(StatusCodes, SpeciesListResponse?)> ListSpeciesAsync(
        ListSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing all species with filters - Category: {Category}, IsPlayable: {IsPlayable}",
            body.Category, body.IsPlayable);

        var allSpeciesIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Species).GetAsync(ALL_SPECIES_KEY, cancellationToken: cancellationToken);

        if (allSpeciesIds == null || allSpeciesIds.Count == 0)
        {
            return (StatusCodes.OK, new SpeciesListResponse
            {
                Species = new List<SpeciesResponse>(),
                TotalCount = 0,
                Page = body.Page,
                PageSize = body.PageSize
            });
        }

        var speciesList = await LoadSpeciesByIdsAsync(allSpeciesIds, cancellationToken);

        // Apply filters
        var filtered = speciesList.AsEnumerable();

        // Filter out deprecated unless explicitly included
        if (body.IncludeDeprecated != true)
        {
            filtered = filtered.Where(s => !s.IsDeprecated);
        }

        if (!string.IsNullOrEmpty(body.Category))
        {
            filtered = filtered.Where(s => string.Equals(s.Category, body.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (body.IsPlayable.HasValue)
        {
            filtered = filtered.Where(s => s.IsPlayable == body.IsPlayable.Value);
        }

        var filteredList = filtered.ToList();
        var totalCount = filteredList.Count;

        // Apply pagination
        var page = body.Page;
        var pageSize = body.PageSize;
        var pagedList = filteredList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToResponse)
            .ToList();

        return (StatusCodes.OK, new SpeciesListResponse
        {
            Species = pagedList,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<(StatusCodes, SpeciesListResponse?)> ListSpeciesByRealmAsync(
        ListSpeciesByRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing species by realm: {RealmId}", body.RealmId);

        // Validate realm exists (regardless of deprecation status - allow viewing deprecated realm species)
        var (realmExists, _) = await ValidateRealmAsync(body.RealmId, cancellationToken);
        if (!realmExists)
        {
            _logger.LogDebug("Realm not found: {RealmId}", body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        var realmIndexKey = BuildRealmIndexKey(body.RealmId);
        var speciesIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Species).GetAsync(realmIndexKey, cancellationToken: cancellationToken);

        if (speciesIds == null || speciesIds.Count == 0)
        {
            return (StatusCodes.OK, new SpeciesListResponse
            {
                Species = new List<SpeciesResponse>(),
                TotalCount = 0,
                Page = body.Page,
                PageSize = body.PageSize
            });
        }

        var speciesList = await LoadSpeciesByIdsAsync(speciesIds, cancellationToken);

        // Apply filters
        var filtered = speciesList.AsEnumerable();

        if (body.IsPlayable.HasValue)
        {
            filtered = filtered.Where(s => s.IsPlayable == body.IsPlayable.Value);
        }

        var filteredList = filtered.ToList();
        var totalCount = filteredList.Count;

        // Apply pagination
        var page = body.Page;
        var pageSize = body.PageSize;
        var pagedList = filteredList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToResponse)
            .ToList();

        return (StatusCodes.OK, new SpeciesListResponse
        {
            Species = pagedList,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    #endregion

    #region Write Operations

    public async Task<(StatusCodes, SpeciesResponse?)> CreateSpeciesAsync(
        CreateSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating species with code: {Code}", body.Code);

        var code = body.Code.ToUpperInvariant();

        // Check if code already exists
        var codeIndexKey = BuildCodeIndexKey(code);
        var existingId = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Species).GetAsync(codeIndexKey, cancellationToken: cancellationToken);

        if (!string.IsNullOrEmpty(existingId))
        {
            _logger.LogDebug("Species with code already exists: {Code}", code);
            return (StatusCodes.Conflict, null);
        }

        // Validate all provided realms exist and are active
        if (body.RealmIds != null && body.RealmIds.Count > 0)
        {
            var (invalidRealms, deprecatedRealms) = await ValidateRealmsAsync(body.RealmIds, cancellationToken);

            if (invalidRealms.Count > 0)
            {
                _logger.LogDebug("Cannot create species {Code}: realms not found: {RealmIds}",
                    code, string.Join(", ", invalidRealms));
                return (StatusCodes.BadRequest, null);
            }

            if (deprecatedRealms.Count > 0)
            {
                _logger.LogDebug("Cannot create species {Code}: realms are deprecated: {RealmIds}",
                    code, string.Join(", ", deprecatedRealms));
                return (StatusCodes.BadRequest, null);
            }
        }

        var speciesId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var model = new SpeciesModel
        {
            SpeciesId = speciesId,
            Code = code,
            Name = body.Name,
            Description = body.Description,
            Category = body.Category,
            IsPlayable = body.IsPlayable,
            BaseLifespan = body.BaseLifespan,
            MaturityAge = body.MaturityAge,
            TraitModifiers = body.TraitModifiers,
            RealmIds = body.RealmIds?.ToList() ?? new List<Guid>(),
            Metadata = body.Metadata,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Save the model
        var speciesKey = BuildSpeciesKey(speciesId);
        await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);

        // Update code index
        await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Species).SaveAsync(codeIndexKey, speciesId.ToString(), cancellationToken: cancellationToken);

        // Update all-species list
        var allSpeciesStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Species);
        var allSpeciesIds = await allSpeciesStore.GetAsync(ALL_SPECIES_KEY, cancellationToken) ?? new List<Guid>();
        if (!allSpeciesIds.Contains(speciesId))
        {
            allSpeciesIds.Add(speciesId);
            await allSpeciesStore.SaveAsync(ALL_SPECIES_KEY, allSpeciesIds, cancellationToken: cancellationToken);
        }

        // Update realm indexes
        foreach (var realmId in model.RealmIds)
        {
            await AddToRealmIndexAsync(speciesId, realmId, cancellationToken);
        }

        // Publish species created event
        await PublishSpeciesCreatedEventAsync(model, cancellationToken);

        _logger.LogInformation("Created species: {SpeciesId} with code {Code}", speciesId, code);
        return (StatusCodes.OK, MapToResponse(model));
    }

    public async Task<(StatusCodes, SpeciesResponse?)> UpdateSpeciesAsync(
        UpdateSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating species: {SpeciesId}", body.SpeciesId);

        var speciesKey = BuildSpeciesKey(body.SpeciesId);
        var model = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetAsync(speciesKey, cancellationToken: cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Species not found for update: {SpeciesId}", body.SpeciesId);
            return (StatusCodes.NotFound, null);
        }

        // Track changed fields and apply updates
        var changedFields = ApplySpeciesFieldUpdates(
            model,
            body.Name, body.Description, body.Category,
            body.IsPlayable, body.BaseLifespan, body.MaturityAge,
            body.TraitModifiers, body.Metadata);

        if (changedFields.Count > 0)
        {
            model.UpdatedAt = DateTimeOffset.UtcNow;
            await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);

            // Publish species updated event
            await PublishSpeciesUpdatedEventAsync(model, changedFields, cancellationToken);
        }

        _logger.LogInformation("Updated species: {SpeciesId}", body.SpeciesId);
        return (StatusCodes.OK, MapToResponse(model));
    }

    public async Task<StatusCodes> DeleteSpeciesAsync(
        DeleteSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting species: {SpeciesId}", body.SpeciesId);

        var speciesKey = BuildSpeciesKey(body.SpeciesId);
        var model = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetAsync(speciesKey, cancellationToken: cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Species not found for deletion: {SpeciesId}", body.SpeciesId);
            return StatusCodes.NotFound;
        }

        // Species must be deprecated before deletion (per schema: "Only deprecated species with zero references can be deleted")
        if (!model.IsDeprecated)
        {
            _logger.LogDebug("Cannot delete non-deprecated species {Code}: must deprecate first", model.Code);
            return StatusCodes.BadRequest;
        }

        // Check if species is in use by characters
        try
        {
            var charactersResponse = await _characterClient.ListCharactersAsync(
                new ListCharactersRequest { SpeciesId = body.SpeciesId, Page = 1, PageSize = 1 },
                cancellationToken);

            if (charactersResponse.TotalCount > 0)
            {
                _logger.LogDebug("Cannot delete species {Code}: {Count} characters use this species",
                    model.Code, charactersResponse.TotalCount);
                return StatusCodes.Conflict;
            }
        }
        catch (ApiException ex)
        {
            // CharacterService returned an error - fail closed, could cause data integrity issues
            _logger.LogWarning(ex, "Character service error verifying usage for species {Code}: {StatusCode} - failing deletion (fail closed)",
                model.Code, ex.StatusCode);
            throw new InvalidOperationException($"Cannot verify character usage for species {model.Code}: CharacterService returned {ex.StatusCode}", ex);
        }
        catch (Exception ex)
        {
            // If CharacterService is unavailable, fail the operation - could cause data integrity issues
            _logger.LogError(ex, "Could not verify character usage for species {Code} - failing deletion (fail closed)", model.Code);
            throw new InvalidOperationException($"Cannot verify character usage for species {model.Code}: CharacterService unavailable", ex);
        }

        // Delete the model
        await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).DeleteAsync(speciesKey, cancellationToken);

        // Delete code index
        var codeIndexKey = BuildCodeIndexKey(model.Code);
        await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Species).DeleteAsync(codeIndexKey, cancellationToken);

        // Remove from all-species list
        var allSpeciesIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Species).GetAsync(ALL_SPECIES_KEY, cancellationToken: cancellationToken) ?? new List<Guid>();
        if (allSpeciesIds.Remove(body.SpeciesId))
        {
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Species).SaveAsync(ALL_SPECIES_KEY, allSpeciesIds, cancellationToken: cancellationToken);
        }

        // Remove from realm indexes
        foreach (var realmId in model.RealmIds)
        {
            await RemoveFromRealmIndexAsync(body.SpeciesId, realmId, cancellationToken);
        }

        // Publish species deleted event
        await PublishSpeciesDeletedEventAsync(model, null, cancellationToken);

        _logger.LogInformation("Deleted species: {SpeciesId} ({Code})", body.SpeciesId, model.Code);
        return StatusCodes.OK;
    }

    #endregion

    #region Realm Association Operations

    public async Task<(StatusCodes, SpeciesResponse?)> AddSpeciesToRealmAsync(
        AddSpeciesToRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Adding species {SpeciesId} to realm {RealmId}", body.SpeciesId, body.RealmId);

        // Validate realm exists and is active
        var (realmExists, realmIsActive) = await ValidateRealmAsync(body.RealmId, cancellationToken);
        if (!realmExists)
        {
            _logger.LogDebug("Realm not found: {RealmId}", body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        if (!realmIsActive)
        {
            _logger.LogDebug("Cannot add species to deprecated realm: {RealmId}", body.RealmId);
            return (StatusCodes.BadRequest, null);
        }

        var speciesKey = BuildSpeciesKey(body.SpeciesId);
        var model = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetAsync(speciesKey, cancellationToken: cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Species not found: {SpeciesId}", body.SpeciesId);
            return (StatusCodes.NotFound, null);
        }

        if (model.RealmIds.Contains(body.RealmId))
        {
            _logger.LogDebug("Species {SpeciesId} already in realm {RealmId}", body.SpeciesId, body.RealmId);
            return (StatusCodes.Conflict, null);
        }

        model.RealmIds.Add(body.RealmId);
        model.UpdatedAt = DateTimeOffset.UtcNow;

        await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);
        await AddToRealmIndexAsync(body.SpeciesId, body.RealmId, cancellationToken);

        await PublishSpeciesUpdatedEventAsync(model, new[] { "realmIds" }, cancellationToken);

        _logger.LogInformation("Added species {SpeciesId} to realm {RealmId}", body.SpeciesId, body.RealmId);
        return (StatusCodes.OK, MapToResponse(model));
    }

    public async Task<(StatusCodes, SpeciesResponse?)> RemoveSpeciesFromRealmAsync(
        RemoveSpeciesFromRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Removing species {SpeciesId} from realm {RealmId}", body.SpeciesId, body.RealmId);

        var speciesKey = BuildSpeciesKey(body.SpeciesId);
        var model = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetAsync(speciesKey, cancellationToken: cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Species not found: {SpeciesId}", body.SpeciesId);
            return (StatusCodes.NotFound, null);
        }

        if (!model.RealmIds.Contains(body.RealmId))
        {
            _logger.LogDebug("Species {SpeciesId} not in realm {RealmId}", body.SpeciesId, body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        // Check if characters of this species exist in the realm
        try
        {
            var charactersResponse = await _characterClient.GetCharactersByRealmAsync(
                new GetCharactersByRealmRequest
                {
                    RealmId = body.RealmId,
                    SpeciesId = body.SpeciesId,
                    Page = 1,
                    PageSize = 1
                },
                cancellationToken);

            if (charactersResponse.TotalCount > 0)
            {
                _logger.LogDebug("Cannot remove species {Code} from realm {RealmId}: {Count} characters use this species in this realm",
                    model.Code, body.RealmId, charactersResponse.TotalCount);
                return (StatusCodes.Conflict, null);
            }
        }
        catch (ApiException ex)
        {
            // CharacterService returned an error - fail closed, could cause data integrity issues
            _logger.LogWarning(ex, "Character service error verifying usage for species {Code} in realm {RealmId}: {StatusCode} - failing removal (fail closed)",
                model.Code, body.RealmId, ex.StatusCode);
            throw new InvalidOperationException($"Cannot verify character usage for species {model.Code} in realm {body.RealmId}: CharacterService returned {ex.StatusCode}", ex);
        }
        catch (Exception ex)
        {
            // If CharacterService is unavailable, fail the operation - could cause data integrity issues
            _logger.LogError(ex, "Could not verify character usage for species {Code} in realm {RealmId} - failing removal (fail closed)",
                model.Code, body.RealmId);
            throw new InvalidOperationException($"Cannot verify character usage for species {model.Code} in realm {body.RealmId}: CharacterService unavailable", ex);
        }

        model.RealmIds.Remove(body.RealmId);
        model.UpdatedAt = DateTimeOffset.UtcNow;

        await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);
        await RemoveFromRealmIndexAsync(body.SpeciesId, body.RealmId, cancellationToken);

        await PublishSpeciesUpdatedEventAsync(model, new[] { "realmIds" }, cancellationToken);

        _logger.LogInformation("Removed species {SpeciesId} from realm {RealmId}", body.SpeciesId, body.RealmId);
        return (StatusCodes.OK, MapToResponse(model));
    }

    #endregion

    #region Seed Operation

    public async Task<(StatusCodes, SeedSpeciesResponse?)> SeedSpeciesAsync(
        SeedSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Seeding {Count} species, updateExisting: {UpdateExisting}",
            body.Species.Count, body.UpdateExisting);

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var seedSpecies in body.Species)
        {
            try
            {
                var code = seedSpecies.Code.ToUpperInvariant();
                var codeIndexKey = BuildCodeIndexKey(code);
                var existingIdStr = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Species).GetAsync(codeIndexKey, cancellationToken: cancellationToken);

                if (!string.IsNullOrEmpty(existingIdStr) && Guid.TryParse(existingIdStr, out var existingId))
                {
                    if (body.UpdateExisting)
                    {
                        // Update existing
                        var speciesKey = BuildSpeciesKey(existingId);
                        var existingModel = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetAsync(speciesKey, cancellationToken: cancellationToken);

                        if (existingModel != null)
                        {
                            var changedFields = ApplySpeciesFieldUpdates(
                                existingModel,
                                seedSpecies.Name, seedSpecies.Description, seedSpecies.Category,
                                seedSpecies.IsPlayable, seedSpecies.BaseLifespan, seedSpecies.MaturityAge,
                                seedSpecies.TraitModifiers, seedSpecies.Metadata);

                            if (changedFields.Count > 0)
                            {
                                existingModel.UpdatedAt = DateTimeOffset.UtcNow;
                                await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).SaveAsync(speciesKey, existingModel, cancellationToken: cancellationToken);

                                // Publish updated event for changed species
                                await PublishSpeciesUpdatedEventAsync(existingModel, changedFields, cancellationToken);
                                updated++;
                                _logger.LogDebug("Updated existing species: {Code} (changed: {ChangedFields})", code, string.Join(", ", changedFields));
                            }
                            else
                            {
                                skipped++;
                                _logger.LogDebug("Skipped unchanged species: {Code}", code);
                            }
                        }
                    }
                    else
                    {
                        skipped++;
                        _logger.LogDebug("Skipped existing species: {Code}", code);
                    }
                }
                else
                {
                    // Resolve realm codes to IDs if provided
                    var resolvedRealmIds = new List<Guid>();
                    if (seedSpecies.RealmCodes != null && seedSpecies.RealmCodes.Count > 0)
                    {
                        foreach (var realmCode in seedSpecies.RealmCodes)
                        {
                            try
                            {
                                var realmResponse = await _realmClient.GetRealmByCodeAsync(
                                    new GetRealmByCodeRequest { Code = realmCode },
                                    cancellationToken);
                                resolvedRealmIds.Add(realmResponse.RealmId);
                            }
                            catch (ApiException ex) when (ex.StatusCode == 404)
                            {
                                _logger.LogWarning("Realm code {RealmCode} not found during seed for species {SpeciesCode}, skipping realm assignment",
                                    realmCode, code);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to resolve realm code {RealmCode} during seed for species {SpeciesCode}, skipping realm assignment",
                                    realmCode, code);
                            }
                        }
                    }

                    // Create new
                    var createRequest = new CreateSpeciesRequest
                    {
                        Code = code,
                        Name = seedSpecies.Name,
                        Description = seedSpecies.Description,
                        Category = seedSpecies.Category,
                        IsPlayable = seedSpecies.IsPlayable,
                        BaseLifespan = seedSpecies.BaseLifespan,
                        MaturityAge = seedSpecies.MaturityAge,
                        TraitModifiers = seedSpecies.TraitModifiers,
                        Metadata = seedSpecies.Metadata,
                        RealmIds = resolvedRealmIds
                    };

                    var (status, _) = await CreateSpeciesAsync(createRequest, cancellationToken);

                    if (status == StatusCodes.OK)
                    {
                        created++;
                        _logger.LogDebug("Created new species: {Code}", code);
                    }
                    else
                    {
                        errors.Add($"Failed to create species {code}: {status}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Error processing species {seedSpecies.Code}: {ex.Message}");
                _logger.LogWarning(ex, "Error seeding species: {Code}", seedSpecies.Code);
            }
        }

        _logger.LogInformation("Seed complete - Created: {Created}, Updated: {Updated}, Skipped: {Skipped}, Errors: {Errors}",
            created, updated, skipped, errors.Count);

        return (StatusCodes.OK, new SeedSpeciesResponse
        {
            Created = created,
            Updated = updated,
            Skipped = skipped,
            Errors = errors
        });
    }

    #endregion

    #region Deprecation Operations

    public async Task<(StatusCodes, SpeciesResponse?)> DeprecateSpeciesAsync(
        DeprecateSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deprecating species: {SpeciesId}", body.SpeciesId);

        var speciesKey = BuildSpeciesKey(body.SpeciesId);
        var model = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetAsync(speciesKey, cancellationToken: cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Species not found for deprecation: {SpeciesId}", body.SpeciesId);
            return (StatusCodes.NotFound, null);
        }

        // Idempotent per IMPLEMENTATION TENETS — caller's intent (deprecate) is already satisfied
        if (model.IsDeprecated)
        {
            _logger.LogDebug("Species {SpeciesId} already deprecated, returning OK (idempotent)", body.SpeciesId);
            return (StatusCodes.OK, MapToResponse(model));
        }

        model.IsDeprecated = true;
        model.DeprecatedAt = DateTimeOffset.UtcNow;
        model.DeprecationReason = body.Reason;
        model.UpdatedAt = DateTimeOffset.UtcNow;

        await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);

        // Publish species updated event with deprecation fields
        await PublishSpeciesUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

        _logger.LogInformation("Deprecated species: {SpeciesId}", body.SpeciesId);
        return (StatusCodes.OK, MapToResponse(model));
    }

    public async Task<(StatusCodes, SpeciesResponse?)> UndeprecateSpeciesAsync(
        UndeprecateSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Undeprecating species: {SpeciesId}", body.SpeciesId);

        var speciesKey = BuildSpeciesKey(body.SpeciesId);
        var model = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetAsync(speciesKey, cancellationToken: cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Species not found for undeprecation: {SpeciesId}", body.SpeciesId);
            return (StatusCodes.NotFound, null);
        }

        // Idempotent per IMPLEMENTATION TENETS — caller's intent (undeprecate) is already satisfied
        if (!model.IsDeprecated)
        {
            _logger.LogDebug("Species {SpeciesId} not deprecated, returning OK (idempotent)", body.SpeciesId);
            return (StatusCodes.OK, MapToResponse(model));
        }

        model.IsDeprecated = false;
        model.DeprecatedAt = null;
        model.DeprecationReason = null;
        model.UpdatedAt = DateTimeOffset.UtcNow;

        await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);

        // Publish species updated event with deprecation fields cleared
        await PublishSpeciesUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

        _logger.LogInformation("Undeprecated species: {SpeciesId}", body.SpeciesId);
        return (StatusCodes.OK, MapToResponse(model));
    }

    public async Task<(StatusCodes, MergeSpeciesResponse?)> MergeSpeciesAsync(
        MergeSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Merging species {SourceId} into {TargetId}", body.SourceSpeciesId, body.TargetSpeciesId);

        // Verify source exists and is deprecated
        var sourceKey = BuildSpeciesKey(body.SourceSpeciesId);
        var sourceModel = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetAsync(sourceKey, cancellationToken: cancellationToken);

        if (sourceModel == null)
        {
            _logger.LogDebug("Source species not found: {SpeciesId}", body.SourceSpeciesId);
            return (StatusCodes.NotFound, null);
        }

        if (!sourceModel.IsDeprecated)
        {
            _logger.LogDebug("Source species must be deprecated before merging: {SpeciesId}", body.SourceSpeciesId);
            return (StatusCodes.BadRequest, null);
        }

        // Verify target exists
        var targetKey = BuildSpeciesKey(body.TargetSpeciesId);
        var targetModel = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetAsync(targetKey, cancellationToken: cancellationToken);

        if (targetModel == null)
        {
            _logger.LogDebug("Target species not found: {SpeciesId}", body.TargetSpeciesId);
            return (StatusCodes.NotFound, null);
        }

        // Migrate all characters from source species to target species
        var migratedCount = 0;
        var failedCount = 0;
        var page = 1;
        var pageSize = _configuration.MergePageSize;
        var hasMorePages = true;

        while (hasMorePages)
        {
            try
            {
                var charactersResponse = await _characterClient.ListCharactersAsync(
                    new ListCharactersRequest { SpeciesId = body.SourceSpeciesId, Page = page, PageSize = pageSize },
                    cancellationToken);

                if (charactersResponse.Characters == null || charactersResponse.Characters.Count == 0)
                {
                    hasMorePages = false;
                    continue;
                }

                // Migrate each character to the target species
                foreach (var character in charactersResponse.Characters)
                {
                    try
                    {
                        await _characterClient.UpdateCharacterAsync(
                            new UpdateCharacterRequest
                            {
                                CharacterId = character.CharacterId,
                                SpeciesId = body.TargetSpeciesId
                            },
                            cancellationToken);
                        migratedCount++;
                    }
                    catch (ApiException charEx)
                    {
                        _logger.LogWarning(charEx, "Character service error migrating character {CharacterId}: {StatusCode}",
                            character.CharacterId, charEx.StatusCode);
                        failedCount++;
                    }
                    catch (Exception charEx)
                    {
                        _logger.LogWarning(charEx, "Failed to migrate character {CharacterId} from species {SourceId} to {TargetId}",
                            character.CharacterId, body.SourceSpeciesId, body.TargetSpeciesId);
                        failedCount++;
                    }
                }

                hasMorePages = charactersResponse.HasNextPage;
                page++;
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "Character service error fetching characters for species migration at page {Page}: {StatusCode}, aborting merge after {MigratedCount} migrated",
                    page, ex.StatusCode, migratedCount);
                await _messageBus.TryPublishErrorAsync(
                    "species", "MergeSpecies", "page_fetch_failed", ex.Message,
                    dependency: "character", endpoint: "post:/character/list",
                    details: $"Page={page}, MigratedSoFar={migratedCount}, FailedSoFar={failedCount}",
                    stack: ex.StackTrace, cancellationToken: cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching characters for species migration at page {Page}, aborting merge after {MigratedCount} migrated",
                    page, migratedCount);
                await _messageBus.TryPublishErrorAsync(
                    "species", "MergeSpecies", "page_fetch_failed", ex.Message,
                    dependency: "character", endpoint: "post:/character/list",
                    details: $"Page={page}, MigratedSoFar={migratedCount}, FailedSoFar={failedCount}",
                    stack: ex.StackTrace, cancellationToken: cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }
        }

        if (failedCount > 0)
        {
            _logger.LogWarning("Species merge completed with {FailedCount} failed character migrations", failedCount);
        }

        _logger.LogInformation("Merged species {SourceId} into {TargetId}, migrated {MigratedCount} characters (failed: {FailedCount})",
            body.SourceSpeciesId, body.TargetSpeciesId, migratedCount, failedCount);

        // Publish species merged event for downstream services (analytics, achievements)
        await PublishSpeciesMergedEventAsync(sourceModel, targetModel, migratedCount, cancellationToken);

        // Handle delete-after-merge if requested and all migrations succeeded
        var sourceDeleted = false;
        if (body.DeleteAfterMerge)
        {
            if (failedCount > 0)
            {
                _logger.LogWarning("Skipping delete-after-merge for species {SourceId}: {FailedCount} character migrations failed",
                    body.SourceSpeciesId, failedCount);
            }
            else
            {
                var deleteResult = await DeleteSpeciesAsync(
                    new DeleteSpeciesRequest { SpeciesId = body.SourceSpeciesId },
                    cancellationToken);

                if (deleteResult == StatusCodes.OK)
                {
                    sourceDeleted = true;
                }
                else
                {
                    _logger.LogWarning("Delete-after-merge failed for species {SourceId} with status {Status}",
                        body.SourceSpeciesId, deleteResult);
                }
            }
        }

        return (StatusCodes.OK, new MergeSpeciesResponse
        {
            SourceSpeciesId = body.SourceSpeciesId,
            TargetSpeciesId = body.TargetSpeciesId,
            CharactersMigrated = migratedCount,
            SourceDeleted = sourceDeleted
        });
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Applies field updates to an existing species model and returns the list of changed field names.
    /// Used by both UpdateSpeciesAsync and SeedSpeciesAsync (updateExisting path) to avoid
    /// duplicating change-tracking logic.
    /// </summary>
    private static List<string> ApplySpeciesFieldUpdates(
        SpeciesModel model,
        string? name,
        string? description,
        string? category,
        bool? isPlayable,
        int? baseLifespan,
        int? maturityAge,
        object? traitModifiers,
        object? metadata)
    {
        var changedFields = new List<string>();

        if (name != null && name != model.Name)
        {
            model.Name = name;
            changedFields.Add("name");
        }
        if (description != null && description != model.Description)
        {
            model.Description = description;
            changedFields.Add("description");
        }
        if (category != null && category != model.Category)
        {
            model.Category = category;
            changedFields.Add("category");
        }
        if (isPlayable.HasValue && isPlayable.Value != model.IsPlayable)
        {
            model.IsPlayable = isPlayable.Value;
            changedFields.Add("isPlayable");
        }
        if (baseLifespan.HasValue && baseLifespan != model.BaseLifespan)
        {
            model.BaseLifespan = baseLifespan;
            changedFields.Add("baseLifespan");
        }
        if (maturityAge.HasValue && maturityAge != model.MaturityAge)
        {
            model.MaturityAge = maturityAge;
            changedFields.Add("maturityAge");
        }
        if (traitModifiers != null)
        {
            model.TraitModifiers = traitModifiers;
            changedFields.Add("traitModifiers");
        }
        if (metadata != null)
        {
            model.Metadata = metadata;
            changedFields.Add("metadata");
        }

        return changedFields;
    }

    private async Task<List<SpeciesModel>> LoadSpeciesByIdsAsync(List<Guid> speciesIds, CancellationToken cancellationToken)
    {
        if (speciesIds.Count == 0)
        {
            return new List<SpeciesModel>();
        }

        var keys = speciesIds.Select(BuildSpeciesKey).ToList();
        var bulkResults = await _stateStoreFactory.GetStore<SpeciesModel>(StateStoreDefinitions.Species).GetBulkAsync(keys, cancellationToken);

        var speciesList = new List<SpeciesModel>();
        foreach (var (key, model) in bulkResults)
        {
            if (model != null)
            {
                speciesList.Add(model);
            }
        }

        return speciesList;
    }

    private async Task AddToRealmIndexAsync(Guid speciesId, Guid realmId, CancellationToken cancellationToken)
    {
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var speciesIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Species).GetAsync(realmIndexKey, cancellationToken: cancellationToken) ?? new List<Guid>();

        if (!speciesIds.Contains(speciesId))
        {
            speciesIds.Add(speciesId);
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Species).SaveAsync(realmIndexKey, speciesIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRealmIndexAsync(Guid speciesId, Guid realmId, CancellationToken cancellationToken)
    {
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var speciesIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Species).GetAsync(realmIndexKey, cancellationToken: cancellationToken) ?? new List<Guid>();

        if (speciesIds.Remove(speciesId))
        {
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Species).SaveAsync(realmIndexKey, speciesIds, cancellationToken: cancellationToken);
        }
    }

    private static SpeciesResponse MapToResponse(SpeciesModel model)
    {
        return new SpeciesResponse
        {
            SpeciesId = model.SpeciesId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            Category = model.Category,
            IsPlayable = model.IsPlayable,
            BaseLifespan = model.BaseLifespan,
            MaturityAge = model.MaturityAge,
            TraitModifiers = model.TraitModifiers,
            RealmIds = model.RealmIds.ToList(),
            Metadata = model.Metadata,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    #endregion

    #region Event Publishing

    /// <summary>
    /// Publishes a species created event with full entity state.
    /// </summary>
    private async Task PublishSpeciesCreatedEventAsync(SpeciesModel model, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new SpeciesCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SpeciesId = model.SpeciesId,
                Code = model.Code,
                Name = model.Name,
                Description = model.Description,
                Category = model.Category,
                IsPlayable = model.IsPlayable,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt,
                DeprecationReason = model.DeprecationReason,
                BaseLifespan = model.BaseLifespan ?? 0,
                MaturityAge = model.MaturityAge ?? 0,
                TraitModifiers = model.TraitModifiers ?? new Dictionary<string, object>(),
                RealmIds = model.RealmIds?.ToList() ?? [],
                Metadata = model.Metadata,
                CreatedAt = model.CreatedAt,
                UpdatedAt = model.UpdatedAt
            };

            await _messageBus.TryPublishAsync("species.created", eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published species.created event for {SpeciesId}", model.SpeciesId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish species.created event for {SpeciesId}", model.SpeciesId);
        }
    }

    /// <summary>
    /// Publishes a species updated event with current state and changed fields.
    /// Used for all update operations including deprecation, restoration, and realm changes.
    /// </summary>
    private async Task PublishSpeciesUpdatedEventAsync(SpeciesModel model, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new SpeciesUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SpeciesId = model.SpeciesId,
                Code = model.Code,
                Name = model.Name,
                Description = model.Description,
                Category = model.Category,
                IsPlayable = model.IsPlayable,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt,
                DeprecationReason = model.DeprecationReason,
                BaseLifespan = model.BaseLifespan ?? 0,
                MaturityAge = model.MaturityAge ?? 0,
                TraitModifiers = model.TraitModifiers ?? new Dictionary<string, object>(),
                RealmIds = model.RealmIds?.ToList() ?? [],
                Metadata = model.Metadata,
                CreatedAt = model.CreatedAt,
                UpdatedAt = model.UpdatedAt,
                ChangedFields = changedFields.ToList()
            };

            await _messageBus.TryPublishAsync("species.updated", eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published species.updated event for {SpeciesId} with changed fields: {ChangedFields}",
                model.SpeciesId, string.Join(", ", changedFields));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish species.updated event for {SpeciesId}", model.SpeciesId);
        }
    }

    /// <summary>
    /// Publishes a species deleted event with final state before deletion.
    /// </summary>
    private async Task PublishSpeciesDeletedEventAsync(SpeciesModel model, string? deletedReason, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new SpeciesDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SpeciesId = model.SpeciesId,
                Code = model.Code,
                Name = model.Name,
                Description = model.Description,
                Category = model.Category,
                IsPlayable = model.IsPlayable,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt,
                DeprecationReason = model.DeprecationReason,
                BaseLifespan = model.BaseLifespan ?? 0,
                MaturityAge = model.MaturityAge ?? 0,
                TraitModifiers = model.TraitModifiers ?? new Dictionary<string, object>(),
                RealmIds = model.RealmIds?.ToList() ?? [],
                Metadata = model.Metadata,
                CreatedAt = model.CreatedAt,
                UpdatedAt = model.UpdatedAt,
                DeletedReason = deletedReason
            };

            await _messageBus.TryPublishAsync("species.deleted", eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published species.deleted event for {SpeciesId}", model.SpeciesId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish species.deleted event for {SpeciesId}", model.SpeciesId);
        }
    }

    /// <summary>
    /// Publishes a species merged event when one species is merged into another.
    /// </summary>
    private async Task PublishSpeciesMergedEventAsync(
        SpeciesModel sourceModel,
        SpeciesModel targetModel,
        int migratedCharacterCount,
        CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new SpeciesMergedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SourceSpeciesId = sourceModel.SpeciesId,
                SourceSpeciesCode = sourceModel.Code,
                TargetSpeciesId = targetModel.SpeciesId,
                TargetSpeciesCode = targetModel.Code,
                MergedCharacterCount = migratedCharacterCount
            };

            await _messageBus.TryPublishAsync("species.merged", eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published species.merged event: {SourceId} -> {TargetId} ({Count} characters)",
                sourceModel.SpeciesId, targetModel.SpeciesId, migratedCharacterCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish species.merged event for {SourceId} -> {TargetId}",
                sourceModel.SpeciesId, targetModel.SpeciesId);
        }
    }

    #endregion

    #region Permission Registration

    #endregion
}

/// <summary>
/// Internal storage model for species data.
/// </summary>
internal class SpeciesModel
{
    public Guid SpeciesId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool IsPlayable { get; set; } = true;
    public int? BaseLifespan { get; set; }
    public int? MaturityAge { get; set; }
    public object? TraitModifiers { get; set; }
    public List<Guid> RealmIds { get; set; } = new();
    public object? Metadata { get; set; }
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public string? DeprecationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
