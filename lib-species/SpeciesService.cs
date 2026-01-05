using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Species;

/// <summary>
/// Implementation of the Species service.
/// Manages species definitions for characters in the Arcadia game world.
/// Species are realm-specific, allowing different realms to have distinct populations.
/// </summary>
[BannouService("species", typeof(ISpeciesService), lifetime: ServiceLifetime.Scoped)]
public partial class SpeciesService : ISpeciesService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<SpeciesService> _logger;
    private readonly SpeciesServiceConfiguration _configuration;
    private readonly ICharacterClient _characterClient;
    private readonly IRealmClient _realmClient;

    private const string STATE_STORE = "species-statestore";
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

    private static string BuildSpeciesKey(string speciesId) => $"{SPECIES_KEY_PREFIX}{speciesId}";
    private static string BuildCodeIndexKey(string code) => $"{CODE_INDEX_PREFIX}{code.ToUpperInvariant()}";
    private static string BuildRealmIndexKey(string realmId) => $"{REALM_INDEX_PREFIX}{realmId}";

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
    /// Validates multiple realms and returns lists of invalid and deprecated realm IDs.
    /// </summary>
    private async Task<(List<Guid> invalidRealms, List<Guid> deprecatedRealms)> ValidateRealmsAsync(
        IEnumerable<Guid> realmIds,
        CancellationToken cancellationToken)
    {
        var invalidRealms = new List<Guid>();
        var deprecatedRealms = new List<Guid>();

        foreach (var realmId in realmIds)
        {
            var (exists, isActive) = await ValidateRealmAsync(realmId, cancellationToken);
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
        try
        {
            _logger.LogInformation("Getting species by ID: {SpeciesId}", body.SpeciesId);

            var speciesKey = BuildSpeciesKey(body.SpeciesId.ToString());
            var model = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetAsync(speciesKey, cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Species not found: {SpeciesId}", body.SpeciesId);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting species: {SpeciesId}", body.SpeciesId);
            await _messageBus.TryPublishErrorAsync(
                "species", "GetSpecies", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/get",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, SpeciesResponse?)> GetSpeciesByCodeAsync(
        GetSpeciesByCodeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting species by code: {Code}", body.Code);

            var codeIndexKey = BuildCodeIndexKey(body.Code);
            var speciesId = await _stateStoreFactory.GetStore<string>(STATE_STORE).GetAsync(codeIndexKey, cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(speciesId))
            {
                _logger.LogWarning("Species not found by code: {Code}", body.Code);
                return (StatusCodes.NotFound, null);
            }

            var speciesKey = BuildSpeciesKey(speciesId);
            var model = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetAsync(speciesKey, cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Species data inconsistency - code index exists but species not found: {Code}", body.Code);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting species by code: {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "species", "GetSpeciesByCode", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/get-by-code",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, SpeciesListResponse?)> ListSpeciesAsync(
        ListSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Listing all species with filters - Category: {Category}, IsPlayable: {IsPlayable}",
                body.Category, body.IsPlayable);

            var allSpeciesIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(ALL_SPECIES_KEY, cancellationToken: cancellationToken);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing species");
            await _messageBus.TryPublishErrorAsync(
                "species", "ListSpecies", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/list",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, SpeciesListResponse?)> ListSpeciesByRealmAsync(
        ListSpeciesByRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Listing species by realm: {RealmId}", body.RealmId);

            // Validate realm exists (regardless of deprecation status - allow viewing deprecated realm species)
            var (realmExists, _) = await ValidateRealmAsync(body.RealmId, cancellationToken);
            if (!realmExists)
            {
                _logger.LogWarning("Realm not found: {RealmId}", body.RealmId);
                return (StatusCodes.NotFound, null);
            }

            var realmIndexKey = BuildRealmIndexKey(body.RealmId.ToString());
            var speciesIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(realmIndexKey, cancellationToken: cancellationToken);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing species by realm: {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "species", "ListSpeciesByRealm", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/list-by-realm",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Write Operations

    public async Task<(StatusCodes, SpeciesResponse?)> CreateSpeciesAsync(
        CreateSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating species with code: {Code}", body.Code);

            var code = body.Code.ToUpperInvariant();

            // Check if code already exists
            var codeIndexKey = BuildCodeIndexKey(code);
            var existingId = await _stateStoreFactory.GetStore<string>(STATE_STORE).GetAsync(codeIndexKey, cancellationToken: cancellationToken);

            if (!string.IsNullOrEmpty(existingId))
            {
                _logger.LogWarning("Species with code already exists: {Code}", code);
                return (StatusCodes.Conflict, null);
            }

            // Validate all provided realms exist and are active
            if (body.RealmIds != null && body.RealmIds.Count > 0)
            {
                var (invalidRealms, deprecatedRealms) = await ValidateRealmsAsync(body.RealmIds, cancellationToken);

                if (invalidRealms.Count > 0)
                {
                    _logger.LogWarning("Cannot create species {Code}: realms not found: {RealmIds}",
                        code, string.Join(", ", invalidRealms));
                    return (StatusCodes.BadRequest, null);
                }

                if (deprecatedRealms.Count > 0)
                {
                    _logger.LogWarning("Cannot create species {Code}: realms are deprecated: {RealmIds}",
                        code, string.Join(", ", deprecatedRealms));
                    return (StatusCodes.BadRequest, null);
                }
            }

            var speciesId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var model = new SpeciesModel
            {
                SpeciesId = speciesId.ToString(),
                Code = code,
                Name = body.Name,
                Description = body.Description,
                Category = body.Category,
                IsPlayable = body.IsPlayable,
                BaseLifespan = body.BaseLifespan,
                MaturityAge = body.MaturityAge,
                TraitModifiers = body.TraitModifiers,
                RealmIds = body.RealmIds?.Select(r => r.ToString()).ToList() ?? new List<string>(),
                Metadata = body.Metadata,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save the model
            var speciesKey = BuildSpeciesKey(speciesId.ToString());
            await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);

            // Update code index
            await _stateStoreFactory.GetStore<string>(STATE_STORE).SaveAsync(codeIndexKey, speciesId.ToString(), cancellationToken: cancellationToken);

            // Update all-species list
            var allSpeciesStore = _stateStoreFactory.GetStore<List<string>>(STATE_STORE);
            var allSpeciesIds = await allSpeciesStore.GetAsync(ALL_SPECIES_KEY, cancellationToken) ?? new List<string>();
            if (!allSpeciesIds.Contains(speciesId.ToString()))
            {
                allSpeciesIds.Add(speciesId.ToString());
                await allSpeciesStore.SaveAsync(ALL_SPECIES_KEY, allSpeciesIds, cancellationToken: cancellationToken);
            }

            // Update realm indexes
            foreach (var realmId in model.RealmIds)
            {
                await AddToRealmIndexAsync(speciesId.ToString(), realmId, cancellationToken);
            }

            // Publish species created event
            await PublishSpeciesCreatedEventAsync(model, cancellationToken);

            _logger.LogInformation("Created species: {SpeciesId} with code {Code}", speciesId, code);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating species: {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "species", "CreateSpecies", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/create",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, SpeciesResponse?)> UpdateSpeciesAsync(
        UpdateSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating species: {SpeciesId}", body.SpeciesId);

            var speciesKey = BuildSpeciesKey(body.SpeciesId.ToString());
            var model = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetAsync(speciesKey, cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Species not found for update: {SpeciesId}", body.SpeciesId);
                return (StatusCodes.NotFound, null);
            }

            // Track changed fields and apply updates
            var changedFields = new List<string>();

            if (body.Name != null && body.Name != model.Name)
            {
                model.Name = body.Name;
                changedFields.Add("name");
            }
            if (body.Description != null && body.Description != model.Description)
            {
                model.Description = body.Description;
                changedFields.Add("description");
            }
            if (body.Category != null && body.Category != model.Category)
            {
                model.Category = body.Category;
                changedFields.Add("category");
            }
            if (body.IsPlayable.HasValue && body.IsPlayable.Value != model.IsPlayable)
            {
                model.IsPlayable = body.IsPlayable.Value;
                changedFields.Add("isPlayable");
            }
            if (body.BaseLifespan.HasValue && body.BaseLifespan != model.BaseLifespan)
            {
                model.BaseLifespan = body.BaseLifespan;
                changedFields.Add("baseLifespan");
            }
            if (body.MaturityAge.HasValue && body.MaturityAge != model.MaturityAge)
            {
                model.MaturityAge = body.MaturityAge;
                changedFields.Add("maturityAge");
            }
            if (body.TraitModifiers != null)
            {
                model.TraitModifiers = body.TraitModifiers;
                changedFields.Add("traitModifiers");
            }
            if (body.Metadata != null)
            {
                model.Metadata = body.Metadata;
                changedFields.Add("metadata");
            }

            if (changedFields.Count > 0)
            {
                model.UpdatedAt = DateTimeOffset.UtcNow;
                await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);

                // Publish species updated event
                await PublishSpeciesUpdatedEventAsync(model, changedFields, cancellationToken);
            }

            _logger.LogInformation("Updated species: {SpeciesId}", body.SpeciesId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating species: {SpeciesId}", body.SpeciesId);
            await _messageBus.TryPublishErrorAsync(
                "species", "UpdateSpecies", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/update",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<StatusCodes> DeleteSpeciesAsync(
        DeleteSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting species: {SpeciesId}", body.SpeciesId);

            var speciesKey = BuildSpeciesKey(body.SpeciesId.ToString());
            var model = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetAsync(speciesKey, cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Species not found for deletion: {SpeciesId}", body.SpeciesId);
                return StatusCodes.NotFound;
            }

            // Check if species is in use by characters
            try
            {
                var charactersResponse = await _characterClient.ListCharactersAsync(
                    new ListCharactersRequest { SpeciesId = body.SpeciesId, Page = 1, PageSize = 1 },
                    cancellationToken);

                if (charactersResponse.TotalCount > 0)
                {
                    _logger.LogWarning("Cannot delete species {Code}: {Count} characters use this species",
                        model.Code, charactersResponse.TotalCount);
                    return StatusCodes.Conflict;
                }
            }
            catch (Exception ex)
            {
                // If CharacterService is unavailable, fail the operation - could cause data integrity issues
                _logger.LogError(ex, "Could not verify character usage for species {Code} - failing deletion (fail closed)", model.Code);
                throw new InvalidOperationException($"Cannot verify character usage for species {model.Code}: CharacterService unavailable", ex);
            }

            // Delete the model
            await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).DeleteAsync(speciesKey, cancellationToken);

            // Delete code index
            var codeIndexKey = BuildCodeIndexKey(model.Code);
            await _stateStoreFactory.GetStore<string>(STATE_STORE).DeleteAsync(codeIndexKey, cancellationToken);

            // Remove from all-species list
            var allSpeciesIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(ALL_SPECIES_KEY, cancellationToken: cancellationToken) ?? new List<string>();
            if (allSpeciesIds.Remove(body.SpeciesId.ToString()))
            {
                await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).SaveAsync(ALL_SPECIES_KEY, allSpeciesIds, cancellationToken: cancellationToken);
            }

            // Remove from realm indexes
            foreach (var realmId in model.RealmIds)
            {
                await RemoveFromRealmIndexAsync(body.SpeciesId.ToString(), realmId, cancellationToken);
            }

            // Publish species deleted event
            await PublishSpeciesDeletedEventAsync(model, null, cancellationToken);

            _logger.LogInformation("Deleted species: {SpeciesId} ({Code})", body.SpeciesId, model.Code);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting species: {SpeciesId}", body.SpeciesId);
            await _messageBus.TryPublishErrorAsync(
                "species", "DeleteSpecies", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/delete",
                details: null, stack: ex.StackTrace);
            return StatusCodes.InternalServerError;
        }
    }

    #endregion

    #region Realm Association Operations

    public async Task<(StatusCodes, SpeciesResponse?)> AddSpeciesToRealmAsync(
        AddSpeciesToRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Adding species {SpeciesId} to realm {RealmId}", body.SpeciesId, body.RealmId);

            // Validate realm exists and is active
            var (realmExists, realmIsActive) = await ValidateRealmAsync(body.RealmId, cancellationToken);
            if (!realmExists)
            {
                _logger.LogWarning("Realm not found: {RealmId}", body.RealmId);
                return (StatusCodes.NotFound, null);
            }

            if (!realmIsActive)
            {
                _logger.LogWarning("Cannot add species to deprecated realm: {RealmId}", body.RealmId);
                return (StatusCodes.BadRequest, null);
            }

            var speciesKey = BuildSpeciesKey(body.SpeciesId.ToString());
            var model = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetAsync(speciesKey, cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Species not found: {SpeciesId}", body.SpeciesId);
                return (StatusCodes.NotFound, null);
            }

            var realmIdStr = body.RealmId.ToString();
            if (model.RealmIds.Contains(realmIdStr))
            {
                _logger.LogWarning("Species {SpeciesId} already in realm {RealmId}", body.SpeciesId, body.RealmId);
                return (StatusCodes.Conflict, null);
            }

            model.RealmIds.Add(realmIdStr);
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);
            await AddToRealmIndexAsync(body.SpeciesId.ToString(), realmIdStr, cancellationToken);

            await PublishSpeciesUpdatedEventAsync(model, new[] { "realmIds" }, cancellationToken);

            _logger.LogInformation("Added species {SpeciesId} to realm {RealmId}", body.SpeciesId, body.RealmId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding species to realm");
            await _messageBus.TryPublishErrorAsync(
                "species", "AddSpeciesToRealm", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/add-to-realm",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, SpeciesResponse?)> RemoveSpeciesFromRealmAsync(
        RemoveSpeciesFromRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Removing species {SpeciesId} from realm {RealmId}", body.SpeciesId, body.RealmId);

            var speciesKey = BuildSpeciesKey(body.SpeciesId.ToString());
            var model = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetAsync(speciesKey, cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Species not found: {SpeciesId}", body.SpeciesId);
                return (StatusCodes.NotFound, null);
            }

            var realmIdStr = body.RealmId.ToString();
            if (!model.RealmIds.Contains(realmIdStr))
            {
                _logger.LogWarning("Species {SpeciesId} not in realm {RealmId}", body.SpeciesId, body.RealmId);
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
                    _logger.LogWarning("Cannot remove species {Code} from realm {RealmId}: {Count} characters use this species in this realm",
                        model.Code, body.RealmId, charactersResponse.TotalCount);
                    return (StatusCodes.Conflict, null);
                }
            }
            catch (Exception ex)
            {
                // If CharacterService is unavailable, fail the operation - could cause data integrity issues
                _logger.LogError(ex, "Could not verify character usage for species {Code} in realm {RealmId} - failing removal (fail closed)",
                    model.Code, body.RealmId);
                throw new InvalidOperationException($"Cannot verify character usage for species {model.Code} in realm {body.RealmId}: CharacterService unavailable", ex);
            }

            model.RealmIds.Remove(realmIdStr);
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);
            await RemoveFromRealmIndexAsync(body.SpeciesId.ToString(), realmIdStr, cancellationToken);

            await PublishSpeciesUpdatedEventAsync(model, new[] { "realmIds" }, cancellationToken);

            _logger.LogInformation("Removed species {SpeciesId} from realm {RealmId}", body.SpeciesId, body.RealmId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing species from realm");
            await _messageBus.TryPublishErrorAsync(
                "species", "RemoveSpeciesFromRealm", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/remove-from-realm",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Seed Operation

    public async Task<(StatusCodes, SeedSpeciesResponse?)> SeedSpeciesAsync(
        SeedSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Seeding {Count} species, updateExisting: {UpdateExisting}",
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
                    var existingId = await _stateStoreFactory.GetStore<string>(STATE_STORE).GetAsync(codeIndexKey, cancellationToken: cancellationToken);

                    if (!string.IsNullOrEmpty(existingId))
                    {
                        if (body.UpdateExisting)
                        {
                            // Update existing
                            var speciesKey = BuildSpeciesKey(existingId);
                            var existingModel = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetAsync(speciesKey, cancellationToken: cancellationToken);

                            if (existingModel != null)
                            {
                                existingModel.Name = seedSpecies.Name;
                                if (seedSpecies.Description != null) existingModel.Description = seedSpecies.Description;
                                if (seedSpecies.Category != null) existingModel.Category = seedSpecies.Category;
                                existingModel.IsPlayable = seedSpecies.IsPlayable;
                                existingModel.BaseLifespan = seedSpecies.BaseLifespan;
                                existingModel.MaturityAge = seedSpecies.MaturityAge;
                                if (seedSpecies.TraitModifiers != null) existingModel.TraitModifiers = seedSpecies.TraitModifiers;
                                if (seedSpecies.Metadata != null) existingModel.Metadata = seedSpecies.Metadata;
                                existingModel.UpdatedAt = DateTimeOffset.UtcNow;

                                await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).SaveAsync(speciesKey, existingModel, cancellationToken: cancellationToken);
                                updated++;
                                _logger.LogDebug("Updated existing species: {Code}", code);
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
                            RealmIds = new List<Guid>() // Realm codes would need resolution
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding species");
            await _messageBus.TryPublishErrorAsync(
                "species", "SeedSpecies", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/seed",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Deprecation Operations

    public async Task<(StatusCodes, SpeciesResponse?)> DeprecateSpeciesAsync(
        DeprecateSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deprecating species: {SpeciesId}", body.SpeciesId);

            var speciesKey = BuildSpeciesKey(body.SpeciesId.ToString());
            var model = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetAsync(speciesKey, cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Species not found for deprecation: {SpeciesId}", body.SpeciesId);
                return (StatusCodes.NotFound, null);
            }

            if (model.IsDeprecated)
            {
                _logger.LogWarning("Species already deprecated: {SpeciesId}", body.SpeciesId);
                return (StatusCodes.Conflict, null);
            }

            model.IsDeprecated = true;
            model.DeprecatedAt = DateTimeOffset.UtcNow;
            model.DeprecationReason = body.Reason;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);

            // Publish species updated event with deprecation fields
            await PublishSpeciesUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

            _logger.LogInformation("Deprecated species: {SpeciesId}", body.SpeciesId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deprecating species: {SpeciesId}", body.SpeciesId);
            await _messageBus.TryPublishErrorAsync(
                "species", "DeprecateSpecies", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/deprecate",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, SpeciesResponse?)> UndeprecateSpeciesAsync(
        UndeprecateSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Undeprecating species: {SpeciesId}", body.SpeciesId);

            var speciesKey = BuildSpeciesKey(body.SpeciesId.ToString());
            var model = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetAsync(speciesKey, cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Species not found for undeprecation: {SpeciesId}", body.SpeciesId);
                return (StatusCodes.NotFound, null);
            }

            if (!model.IsDeprecated)
            {
                _logger.LogWarning("Species not deprecated: {SpeciesId}", body.SpeciesId);
                return (StatusCodes.Conflict, null);
            }

            model.IsDeprecated = false;
            model.DeprecatedAt = null;
            model.DeprecationReason = null;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).SaveAsync(speciesKey, model, cancellationToken: cancellationToken);

            // Publish species updated event with deprecation fields cleared
            await PublishSpeciesUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

            _logger.LogInformation("Undeprecated species: {SpeciesId}", body.SpeciesId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undeprecating species: {SpeciesId}", body.SpeciesId);
            await _messageBus.TryPublishErrorAsync(
                "species", "UndeprecateSpecies", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/undeprecate",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, MergeSpeciesResponse?)> MergeSpeciesAsync(
        MergeSpeciesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Merging species {SourceId} into {TargetId}", body.SourceSpeciesId, body.TargetSpeciesId);

            // Verify source exists and is deprecated
            var sourceKey = BuildSpeciesKey(body.SourceSpeciesId.ToString());
            var sourceModel = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetAsync(sourceKey, cancellationToken: cancellationToken);

            if (sourceModel == null)
            {
                _logger.LogWarning("Source species not found: {SpeciesId}", body.SourceSpeciesId);
                return (StatusCodes.NotFound, null);
            }

            if (!sourceModel.IsDeprecated)
            {
                _logger.LogWarning("Source species must be deprecated before merging: {SpeciesId}", body.SourceSpeciesId);
                return (StatusCodes.BadRequest, null);
            }

            // Verify target exists
            var targetKey = BuildSpeciesKey(body.TargetSpeciesId.ToString());
            var targetModel = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetAsync(targetKey, cancellationToken: cancellationToken);

            if (targetModel == null)
            {
                _logger.LogWarning("Target species not found: {SpeciesId}", body.TargetSpeciesId);
                return (StatusCodes.NotFound, null);
            }

            // Migrate all characters from source species to target species
            var migratedCount = 0;
            var failedCount = 0;
            var page = 1;
            const int pageSize = 100;
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
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error fetching characters for species migration at page {Page}", page);
                    hasMorePages = false;
                }
            }

            if (failedCount > 0)
            {
                _logger.LogWarning("Species merge completed with {FailedCount} failed character migrations", failedCount);
            }

            _logger.LogInformation("Merged species {SourceId} into {TargetId}, migrated {MigratedCount} characters (failed: {FailedCount})",
                body.SourceSpeciesId, body.TargetSpeciesId, migratedCount, failedCount);

            return (StatusCodes.OK, new MergeSpeciesResponse
            {
                SourceSpeciesId = body.SourceSpeciesId,
                TargetSpeciesId = body.TargetSpeciesId,
                CharactersMigrated = migratedCount,
                SourceDeleted = false // Source remains as deprecated for historical references
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error merging species {SourceId} into {TargetId}", body.SourceSpeciesId, body.TargetSpeciesId);
            await _messageBus.TryPublishErrorAsync(
                "species", "MergeSpecies", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/species/merge",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Helper Methods

    private async Task<List<SpeciesModel>> LoadSpeciesByIdsAsync(List<string> speciesIds, CancellationToken cancellationToken)
    {
        if (speciesIds.Count == 0)
        {
            return new List<SpeciesModel>();
        }

        var keys = speciesIds.Select(BuildSpeciesKey).ToList();
        var bulkResults = await _stateStoreFactory.GetStore<SpeciesModel>(STATE_STORE).GetBulkAsync(keys, cancellationToken);

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

    private async Task AddToRealmIndexAsync(string speciesId, string realmId, CancellationToken cancellationToken)
    {
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var speciesIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(realmIndexKey, cancellationToken: cancellationToken) ?? new List<string>();

        if (!speciesIds.Contains(speciesId))
        {
            speciesIds.Add(speciesId);
            await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).SaveAsync(realmIndexKey, speciesIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRealmIndexAsync(string speciesId, string realmId, CancellationToken cancellationToken)
    {
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var speciesIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(realmIndexKey, cancellationToken: cancellationToken) ?? new List<string>();

        if (speciesIds.Remove(speciesId))
        {
            await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).SaveAsync(realmIndexKey, speciesIds, cancellationToken: cancellationToken);
        }
    }

    private static SpeciesResponse MapToResponse(SpeciesModel model)
    {
        return new SpeciesResponse
        {
            SpeciesId = Guid.Parse(model.SpeciesId),
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            Category = model.Category,
            IsPlayable = model.IsPlayable,
            BaseLifespan = model.BaseLifespan,
            MaturityAge = model.MaturityAge,
            TraitModifiers = model.TraitModifiers,
            RealmIds = model.RealmIds.Select(Guid.Parse).ToList(),
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
    /// Publishes a species created event.
    /// </summary>
    private async Task PublishSpeciesCreatedEventAsync(SpeciesModel model, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new SpeciesCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SpeciesId = Guid.Parse(model.SpeciesId),
                Code = model.Code,
                Name = model.Name,
                Category = model.Category,
                IsPlayable = model.IsPlayable
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
                SpeciesId = Guid.Parse(model.SpeciesId),
                Code = model.Code,
                Name = model.Name,
                Description = model.Description,
                Category = model.Category,
                IsPlayable = model.IsPlayable,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt ?? default,
                DeprecationReason = model.DeprecationReason,
                BaseLifespan = model.BaseLifespan ?? 0,
                MaturityAge = model.MaturityAge ?? 0,
                TraitModifiers = model.TraitModifiers ?? new Dictionary<string, object>(),
                RealmIds = model.RealmIds?.Select(Guid.Parse).ToList() ?? [],
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
                SpeciesId = Guid.Parse(model.SpeciesId),
                Code = model.Code,
                Name = model.Name,
                Description = model.Description,
                Category = model.Category,
                IsPlayable = model.IsPlayable,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt ?? default,
                DeprecationReason = model.DeprecationReason,
                BaseLifespan = model.BaseLifespan ?? 0,
                MaturityAge = model.MaturityAge ?? 0,
                TraitModifiers = model.TraitModifiers ?? new Dictionary<string, object>(),
                RealmIds = model.RealmIds?.Select(Guid.Parse).ToList() ?? [],
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

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Uses generated permission data from x-permissions sections in the OpenAPI schema.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Species service permissions...");
        await SpeciesPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

    #endregion
}

/// <summary>
/// Internal storage model for species data.
/// </summary>
internal class SpeciesModel
{
    public string SpeciesId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool IsPlayable { get; set; } = true;
    public int? BaseLifespan { get; set; }
    public int? MaturityAge { get; set; }
    public object? TraitModifiers { get; set; }
    public List<string> RealmIds { get; set; } = new();
    public object? Metadata { get; set; }
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public string? DeprecationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
