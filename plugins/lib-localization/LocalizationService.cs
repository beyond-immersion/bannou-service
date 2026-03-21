using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using System.Text;

namespace BeyondImmersion.BannouService.Localization;

/// <summary>
/// Multi-language translation tables with category lifecycle, pronunciation annotations,
/// bulk export, and DI-based key validation for cross-service localization.
/// </summary>
[BannouService("localization", typeof(ILocalizationService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFoundation)]
public partial class LocalizationService : ILocalizationService
{
    private readonly IStateStore<LocalizationCategoryModel> _categoryStore;
    private readonly IQueryableStateStore<LocalizationCategoryModel> _categoryQueryStore;
    private readonly IStateStore<string> _categoryCodeStore;
    private readonly IStateStore<LocalizationEntryModel> _entryStore;
    private readonly IQueryableStateStore<LocalizationEntryModel> _entryQueryStore;
    private readonly IStateStore<string> _compiledCache;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<LocalizationService> _logger;
    private readonly LocalizationServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationService"/> class.
    /// </summary>
    public LocalizationService(
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        IMessageBus messageBus,
        ILogger<LocalizationService> logger,
        LocalizationServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider,
        IEventConsumer eventConsumer)
    {
        _categoryStore = stateStoreFactory.GetStore<LocalizationCategoryModel>(StateStoreDefinitions.LocalizationCategoryStore);
        _categoryQueryStore = stateStoreFactory.GetQueryableStore<LocalizationCategoryModel>(StateStoreDefinitions.LocalizationCategoryStore);
        _categoryCodeStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.LocalizationCategoryStore);
        _entryStore = stateStoreFactory.GetStore<LocalizationEntryModel>(StateStoreDefinitions.LocalizationEntryStore);
        _entryQueryStore = stateStoreFactory.GetQueryableStore<LocalizationEntryModel>(StateStoreDefinitions.LocalizationEntryStore);
        _compiledCache = stateStoreFactory.GetStore<string>(StateStoreDefinitions.LocalizationCompiledCache);
        _lockProvider = lockProvider;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;

        RegisterEventConsumers(eventConsumer);
    }


    /// <summary>
    /// Creates a new runtime localization category.
    /// Returns 409 if a category with the same code already exists.
    /// </summary>
    public async Task<(StatusCodes, CategoryResponse?)> CreateCategoryAsync(CreateCategoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating category with code {Code}", body.Code);

        var lockOwner = $"create-category-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocalizationLock,
            $"category-code:{body.Code}",
            lockOwner,
            _configuration.LockExpirySeconds,
            cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var existingCodeId = await _categoryCodeStore.GetAsync(BuildCategoryCodeKey(body.Code), cancellationToken);
        if (existingCodeId != null)
        {
            _logger.LogDebug("Category code {Code} already exists", body.Code);
            return (StatusCodes.Conflict, null);
        }

        var categoryId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var model = new LocalizationCategoryModel
        {
            CategoryId = categoryId,
            Code = body.Code,
            Description = body.Description,
            IsSchemaDefinition = false,
            ValidationMode = body.ValidationMode ?? _configuration.DefaultValidationMode,
            DefaultLanguage = body.DefaultLanguage ?? _configuration.DefaultLanguage,
            EntryCount = 0,
            CreatedAt = now,
        };

        await _categoryStore.SaveAsync(BuildCategoryKey(categoryId), model, cancellationToken: cancellationToken);
        await _categoryCodeStore.SaveAsync(BuildCategoryCodeKey(body.Code), categoryId.ToString(), cancellationToken: cancellationToken);

        await _messageBus.PublishLocalizationCategoryCreatedAsync(new LocalizationCategoryCreatedEvent
        {
            CategoryId = categoryId,
            Code = model.Code,
            Description = model.Description,
            IsSchemaDefinition = false,
            ValidationMode = model.ValidationMode,
            DefaultLanguage = model.DefaultLanguage,
            EntryCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        }, cancellationToken);

        _logger.LogInformation("Created category {CategoryId} with code {Code}", categoryId, body.Code);

        return (StatusCodes.OK, MapCategoryResponse(model));
    }

    /// <summary>
    /// Retrieves a category by categoryId or code. At least one must be provided.
    /// </summary>
    public async Task<(StatusCodes, CategoryResponse?)> GetCategoryAsync(GetCategoryRequest body, CancellationToken cancellationToken)
    {
        if (body.CategoryId != null)
        {
            var model = await _categoryStore.GetAsync(BuildCategoryKey(body.CategoryId.Value), cancellationToken);
            if (model == null) return (StatusCodes.NotFound, null);
            return (StatusCodes.OK, MapCategoryResponse(model));
        }

        if (body.Code != null)
        {
            var categoryIdStr = await _categoryCodeStore.GetAsync(BuildCategoryCodeKey(body.Code), cancellationToken);
            if (categoryIdStr == null) return (StatusCodes.NotFound, null);

            if (!Guid.TryParse(categoryIdStr, out var categoryId))
                return (StatusCodes.NotFound, null);

            var model = await _categoryStore.GetAsync(BuildCategoryKey(categoryId), cancellationToken);
            if (model == null) return (StatusCodes.NotFound, null);
            return (StatusCodes.OK, MapCategoryResponse(model));
        }

        return (StatusCodes.BadRequest, null);
    }

    /// <summary>
    /// Returns a paginated list of localization categories.
    /// </summary>
    public async Task<(StatusCodes, ListCategoriesResponse?)> ListCategoriesAsync(ListCategoriesRequest body, CancellationToken cancellationToken)
    {
        Expression<Func<LocalizationCategoryModel, bool>>? predicate = body.IncludeSchemaDefinitions
            ? null
            : c => !c.IsSchemaDefinition;

        var page = Math.Max(0, body.Page - 1);
        var pagedResult = await _categoryQueryStore.QueryPagedAsync(
            predicate,
            page,
            body.PageSize,
            cancellationToken: cancellationToken);

        var categories = pagedResult.Items.Select(MapCategoryResponse).ToList();

        return (StatusCodes.OK, new ListCategoriesResponse
        {
            Categories = categories,
            TotalCount = (int)pagedResult.TotalCount,
            Page = body.Page,
            PageSize = body.PageSize,
        });
    }

    /// <summary>
    /// Updates mutable properties of a category (description, validationMode, defaultLanguage).
    /// Code and isSchemaDefinition are immutable.
    /// </summary>
    public async Task<(StatusCodes, CategoryResponse?)> UpdateCategoryAsync(UpdateCategoryRequest body, CancellationToken cancellationToken)
    {
        var lockOwner = $"update-category-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocalizationLock,
            $"category:{body.CategoryId}",
            lockOwner,
            _configuration.LockExpirySeconds,
            cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var model = await _categoryStore.GetAsync(BuildCategoryKey(body.CategoryId), cancellationToken);
        if (model == null) return (StatusCodes.NotFound, null);

        var changedFields = new List<string>();

        if (body.Description != null && body.Description != model.Description)
        {
            model.Description = body.Description;
            changedFields.Add("description");
        }

        if (body.ValidationMode != null && body.ValidationMode != model.ValidationMode)
        {
            model.ValidationMode = body.ValidationMode.Value;
            changedFields.Add("validationMode");
        }

        if (body.DefaultLanguage != null && body.DefaultLanguage != model.DefaultLanguage)
        {
            model.DefaultLanguage = body.DefaultLanguage;
            changedFields.Add("defaultLanguage");
        }

        model.UpdatedAt = DateTimeOffset.UtcNow;

        await _categoryStore.SaveAsync(BuildCategoryKey(body.CategoryId), model, cancellationToken: cancellationToken);

        await _messageBus.PublishLocalizationCategoryUpdatedAsync(new LocalizationCategoryUpdatedEvent
        {
            CategoryId = model.CategoryId,
            Code = model.Code,
            Description = model.Description,
            IsSchemaDefinition = model.IsSchemaDefinition,
            ValidationMode = model.ValidationMode,
            DefaultLanguage = model.DefaultLanguage,
            EntryCount = model.EntryCount,
            LastEntryUpdateLanguage = model.LastEntryUpdateLanguage,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? model.CreatedAt,
            ChangedFields = changedFields,
        }, cancellationToken);

        _logger.LogInformation("Updated category {CategoryId} with changed fields {ChangedFields}", body.CategoryId, changedFields);

        return (StatusCodes.OK, MapCategoryResponse(model));
    }

    /// <summary>
    /// Deletes a runtime category and cascades all entries within it.
    /// Schema-defined categories cannot be deleted and return 400.
    /// </summary>
    public async Task<(StatusCodes, DeleteCategoryResponse?)> DeleteCategoryAsync(DeleteCategoryRequest body, CancellationToken cancellationToken)
    {
        var lockOwner = $"delete-category-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocalizationLock,
            $"category:{body.CategoryId}",
            lockOwner,
            _configuration.LockExpirySeconds,
            cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var model = await _categoryStore.GetAsync(BuildCategoryKey(body.CategoryId), cancellationToken);
        if (model == null) return (StatusCodes.NotFound, null);

        if (model.IsSchemaDefinition)
        {
            _logger.LogDebug("Cannot delete schema-defined category {CategoryId}", body.CategoryId);
            return (StatusCodes.BadRequest, null);
        }

        // Cascade: delete all entries for this category
        var entries = await _entryQueryStore.QueryAsync(
            e => e.CategoryId == body.CategoryId,
            cancellationToken);

        foreach (var entry in entries)
        {
            try
            {
                await _entryStore.DeleteAsync(
                    BuildEntryKey(entry.CategoryId, entry.Language, entry.Key),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete entry {EntryKey} for category {CategoryId}", entry.Key, entry.CategoryId);
            }
        }

        await _categoryStore.DeleteAsync(BuildCategoryKey(body.CategoryId), cancellationToken);
        await _categoryCodeStore.DeleteAsync(BuildCategoryCodeKey(model.Code), cancellationToken);

        // Invalidate all compiled cache entries for this category
        // Delete category-specific caches for all known languages from the entries we already loaded
        var affectedLanguages = entries.Select(e => e.Language).Distinct();
        foreach (var lang in affectedLanguages)
        {
            await _compiledCache.DeleteAsync(BuildCompiledCacheKey(body.CategoryId, lang), cancellationToken);
            await _compiledCache.DeleteAsync(BuildAllCompiledCacheKey(lang), cancellationToken);
        }

        await _messageBus.PublishLocalizationCategoryDeletedAsync(new LocalizationCategoryDeletedEvent
        {
            CategoryId = model.CategoryId,
            Code = model.Code,
            Description = model.Description,
            IsSchemaDefinition = model.IsSchemaDefinition,
            ValidationMode = model.ValidationMode,
            DefaultLanguage = model.DefaultLanguage,
            EntryCount = model.EntryCount,
            LastEntryUpdateLanguage = model.LastEntryUpdateLanguage,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? model.CreatedAt,
        }, cancellationToken);

        _logger.LogInformation("Deleted category {CategoryId} with code {Code}, cascaded {EntryCount} entries",
            body.CategoryId, model.Code, model.EntryCount);

        return (StatusCodes.OK, new DeleteCategoryResponse());
    }

    /// <summary>
    /// Creates or replaces a single translation entry within a category.
    /// Returns 409 if MaxEntriesPerCategory would be exceeded for new entries.
    /// </summary>
    public async Task<(StatusCodes, EntryResponse?)> SetEntryAsync(SetEntryRequest body, CancellationToken cancellationToken)
    {
        var lockOwner = $"set-entry-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocalizationLock,
            $"category:{body.CategoryId}:entries",
            lockOwner,
            _configuration.LockExpirySeconds,
            cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        // Category read inside lock scope — eliminates TOCTOU on EntryCount cap check
        var category = await _categoryStore.GetAsync(BuildCategoryKey(body.CategoryId), cancellationToken);
        if (category == null) return (StatusCodes.NotFound, null);

        var entryKey = BuildEntryKey(body.CategoryId, body.Language, body.Key);
        var existing = await _entryStore.GetAsync(entryKey, cancellationToken);
        var isNew = existing == null;

        if (isNew && category.EntryCount >= _configuration.MaxEntriesPerCategory)
        {
            _logger.LogDebug("Max entries per category exceeded for category {CategoryId}", body.CategoryId);
            return (StatusCodes.Conflict, null);
        }

        var now = DateTimeOffset.UtcNow;
        var entryModel = new LocalizationEntryModel
        {
            EntryId = existing?.EntryId ?? Guid.NewGuid(),
            CategoryId = body.CategoryId,
            Key = body.Key,
            Language = body.Language,
            Text = body.Text,
            Pronunciation = body.Pronunciation,
            Ruby = body.Ruby?.ToArray(),
            UpdatedAt = now,
        };

        await _entryStore.SaveAsync(entryKey, entryModel, cancellationToken: cancellationToken);

        if (isNew)
        {
            var (catModel, etag) = await _categoryStore.GetWithETagAsync(BuildCategoryKey(body.CategoryId), cancellationToken);
            if (catModel != null)
            {
                catModel.EntryCount += 1;
                catModel.LastEntryUpdateLanguage = body.Language;
                catModel.UpdatedAt = now;
                // Compiler satisfaction: etag is non-null when catModel is non-null
                await _categoryStore.TrySaveAsync(BuildCategoryKey(body.CategoryId), catModel, etag ?? string.Empty, cancellationToken: cancellationToken);
            }
        }

        // Invalidate compiled cache for this language
        await _compiledCache.DeleteAsync(BuildCompiledCacheKey(body.CategoryId, body.Language), cancellationToken);
        await _compiledCache.DeleteAsync(BuildAllCompiledCacheKey(body.Language), cancellationToken);

        await _messageBus.PublishLocalizationCategoryUpdatedAsync(new LocalizationCategoryUpdatedEvent
        {
            CategoryId = body.CategoryId,
            Code = category.Code,
            Description = category.Description,
            IsSchemaDefinition = category.IsSchemaDefinition,
            ValidationMode = category.ValidationMode,
            DefaultLanguage = category.DefaultLanguage,
            EntryCount = isNew ? category.EntryCount + 1 : category.EntryCount,
            LastEntryUpdateLanguage = body.Language,
            CreatedAt = category.CreatedAt,
            UpdatedAt = now,
            ChangedFields = new List<string> { "entries" },
        }, cancellationToken);

        _logger.LogInformation("{Action} entry {Key} in category {CategoryId} for language {Language}",
            isNew ? "Created" : "Updated", body.Key, body.CategoryId, body.Language);

        return (StatusCodes.OK, MapEntryResponse(entryModel));
    }

    /// <summary>
    /// Retrieves a single entry by category, language, and key.
    /// </summary>
    public async Task<(StatusCodes, EntryResponse?)> GetEntryAsync(GetEntryRequest body, CancellationToken cancellationToken)
    {
        var category = await _categoryStore.GetAsync(BuildCategoryKey(body.CategoryId), cancellationToken);
        if (category == null) return (StatusCodes.NotFound, null);

        var entry = await _entryStore.GetAsync(BuildEntryKey(body.CategoryId, body.Language, body.Key), cancellationToken);
        if (entry == null) return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, MapEntryResponse(entry));
    }

    /// <summary>
    /// Returns a paginated list of entries filtered by category, optional language, and optional key prefix.
    /// </summary>
    public async Task<(StatusCodes, ListEntriesResponse?)> ListEntriesAsync(ListEntriesRequest body, CancellationToken cancellationToken)
    {
        var category = await _categoryStore.GetAsync(BuildCategoryKey(body.CategoryId), cancellationToken);
        if (category == null) return (StatusCodes.NotFound, null);

        Expression<Func<LocalizationEntryModel, bool>> predicate = e => e.CategoryId == body.CategoryId;

        if (body.Language != null)
        {
            var lang = body.Language;
            predicate = e => e.CategoryId == body.CategoryId && e.Language == lang;
        }

        if (body.Language != null && body.KeyPrefix != null)
        {
            var lang = body.Language;
            var prefix = body.KeyPrefix;
            predicate = e => e.CategoryId == body.CategoryId && e.Language == lang && e.Key.StartsWith(prefix);
        }
        else if (body.KeyPrefix != null)
        {
            var prefix = body.KeyPrefix;
            predicate = e => e.CategoryId == body.CategoryId && e.Key.StartsWith(prefix);
        }

        var page = Math.Max(0, body.Page - 1);
        var pagedResult = await _entryQueryStore.QueryPagedAsync(
            predicate,
            page,
            body.PageSize,
            cancellationToken: cancellationToken);

        var entries = pagedResult.Items.Select(MapEntryResponse).ToList();

        return (StatusCodes.OK, new ListEntriesResponse
        {
            Entries = entries,
            TotalCount = (int)pagedResult.TotalCount,
            Page = body.Page,
            PageSize = body.PageSize,
        });
    }

    /// <summary>
    /// Deletes a single entry by category, language, and key.
    /// </summary>
    public async Task<(StatusCodes, DeleteEntryResponse?)> DeleteEntryAsync(DeleteEntryRequest body, CancellationToken cancellationToken)
    {
        var category = await _categoryStore.GetAsync(BuildCategoryKey(body.CategoryId), cancellationToken);
        if (category == null) return (StatusCodes.NotFound, null);

        var entryKey = BuildEntryKey(body.CategoryId, body.Language, body.Key);
        var entry = await _entryStore.GetAsync(entryKey, cancellationToken);
        if (entry == null) return (StatusCodes.NotFound, null);

        var lockOwner = $"delete-entry-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocalizationLock,
            $"category:{body.CategoryId}:entries",
            lockOwner,
            _configuration.LockExpirySeconds,
            cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        await _entryStore.DeleteAsync(entryKey, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var (catModel, etag) = await _categoryStore.GetWithETagAsync(BuildCategoryKey(body.CategoryId), cancellationToken);
        if (catModel != null)
        {
            catModel.EntryCount = Math.Max(0, catModel.EntryCount - 1);
            catModel.LastEntryUpdateLanguage = body.Language;
            catModel.UpdatedAt = now;
            // Compiler satisfaction: etag is non-null when catModel is non-null
            await _categoryStore.TrySaveAsync(BuildCategoryKey(body.CategoryId), catModel, etag ?? string.Empty, cancellationToken: cancellationToken);
        }

        // Invalidate compiled cache
        await _compiledCache.DeleteAsync(BuildCompiledCacheKey(body.CategoryId, body.Language), cancellationToken);
        await _compiledCache.DeleteAsync(BuildAllCompiledCacheKey(body.Language), cancellationToken);

        await _messageBus.PublishLocalizationCategoryUpdatedAsync(new LocalizationCategoryUpdatedEvent
        {
            CategoryId = body.CategoryId,
            Code = category.Code,
            Description = category.Description,
            IsSchemaDefinition = category.IsSchemaDefinition,
            ValidationMode = category.ValidationMode,
            DefaultLanguage = category.DefaultLanguage,
            EntryCount = Math.Max(0, category.EntryCount - 1),
            LastEntryUpdateLanguage = body.Language,
            CreatedAt = category.CreatedAt,
            UpdatedAt = now,
            ChangedFields = new List<string> { "entries" },
        }, cancellationToken);

        _logger.LogInformation("Deleted entry {Key} from category {CategoryId} for language {Language}",
            body.Key, body.CategoryId, body.Language);

        return (StatusCodes.OK, new DeleteEntryResponse());
    }

    /// <summary>
    /// Upserts multiple entries for a single category and language in one operation.
    /// Per-item error isolation — individual failures do not block the entire batch.
    /// </summary>
    public async Task<(StatusCodes, BulkSetEntriesResponse?)> BulkSetEntriesAsync(BulkSetEntriesRequest body, CancellationToken cancellationToken)
    {
        var category = await _categoryStore.GetAsync(BuildCategoryKey(body.CategoryId), cancellationToken);
        if (category == null) return (StatusCodes.NotFound, null);

        var lockOwner = $"bulk-set-entries-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocalizationLock,
            $"category:{body.CategoryId}:entries",
            lockOwner,
            _configuration.LockExpirySeconds,
            cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var successCount = 0;
        var failureCount = 0;
        var newCount = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var item in body.Entries)
        {
            try
            {
                var entryKey = BuildEntryKey(body.CategoryId, body.Language, item.Key);
                var existing = await _entryStore.GetAsync(entryKey, cancellationToken);
                var isNew = existing == null;

                var entryModel = new LocalizationEntryModel
                {
                    EntryId = existing?.EntryId ?? Guid.NewGuid(),
                    CategoryId = body.CategoryId,
                    Key = item.Key,
                    Language = body.Language,
                    Text = item.Text,
                    Pronunciation = item.Pronunciation,
                    Ruby = item.Ruby?.ToArray(),
                    UpdatedAt = now,
                };

                await _entryStore.SaveAsync(entryKey, entryModel, cancellationToken: cancellationToken);
                if (isNew) newCount++;
                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set entry {Key} in category {CategoryId}", item.Key, body.CategoryId);
                failureCount++;
            }
        }

        // Update entryCount
        var (catModel, etag) = await _categoryStore.GetWithETagAsync(BuildCategoryKey(body.CategoryId), cancellationToken);
        if (catModel != null && newCount > 0)
        {
            catModel.EntryCount += newCount;
            catModel.LastEntryUpdateLanguage = body.Language;
            catModel.UpdatedAt = now;
            // Compiler satisfaction: etag is non-null when catModel is non-null
            await _categoryStore.TrySaveAsync(BuildCategoryKey(body.CategoryId), catModel, etag ?? string.Empty, cancellationToken: cancellationToken);
        }

        // Invalidate compiled cache for affected language
        await _compiledCache.DeleteAsync(BuildCompiledCacheKey(body.CategoryId, body.Language), cancellationToken);
        await _compiledCache.DeleteAsync(BuildAllCompiledCacheKey(body.Language), cancellationToken);

        // Single event for entire batch
        await _messageBus.PublishLocalizationCategoryUpdatedAsync(new LocalizationCategoryUpdatedEvent
        {
            CategoryId = body.CategoryId,
            Code = category.Code,
            Description = category.Description,
            IsSchemaDefinition = category.IsSchemaDefinition,
            ValidationMode = category.ValidationMode,
            DefaultLanguage = category.DefaultLanguage,
            EntryCount = category.EntryCount + newCount,
            LastEntryUpdateLanguage = body.Language,
            CreatedAt = category.CreatedAt,
            UpdatedAt = now,
            ChangedFields = new List<string> { "entries" },
        }, cancellationToken);

        _logger.LogInformation("Bulk set {SuccessCount} entries ({NewCount} new, {FailureCount} failed) in category {CategoryId} for language {Language}",
            successCount, newCount, failureCount, body.CategoryId, body.Language);

        return (StatusCodes.OK, new BulkSetEntriesResponse
        {
            SucceededCount = successCount,
            FailedCount = failureCount,
        });
    }

    /// <summary>
    /// Returns a compiled JSON bundle of all entries for a language, optionally filtered by category.
    /// Results are cached in Redis with configurable TTL.
    /// </summary>
    public async Task<(StatusCodes, ExportResponse?)> ExportLocalizationAsync(ExportRequest body, CancellationToken cancellationToken)
    {
        // Build cache key
        var cacheKey = body.CategoryId != null
            ? BuildCompiledCacheKey(body.CategoryId.Value, body.Language)
            : BuildAllCompiledCacheKey(body.Language);

        // Cache lookup
        var cached = await _compiledCache.GetAsync(cacheKey, cancellationToken);
        if (cached != null)
        {
            var cachedResponse = BannouJson.Deserialize<ExportResponse>(cached);
            if (cachedResponse != null)
                return (StatusCodes.OK, cachedResponse);
        }

        // Cache miss — compile from MySQL
        List<LocalizationCategoryModel> categories;
        if (body.CategoryId != null)
        {
            var category = await _categoryStore.GetAsync(BuildCategoryKey(body.CategoryId.Value), cancellationToken);
            if (category == null) return (StatusCodes.NotFound, null);
            categories = new List<LocalizationCategoryModel> { category };
        }
        else
        {
            var allCategories = await _categoryQueryStore.QueryAsync(c => true, cancellationToken);
            categories = allCategories.ToList();
        }

        var allEntries = new List<ExportedEntry>();
        foreach (var cat in categories)
        {
            var lang = body.Language;
            var catId = cat.CategoryId;

            // Paginated query per ExportPageSize
            var page = 0;
            bool hasMore;
            do
            {
                var pagedResult = await _entryQueryStore.QueryPagedAsync(
                    e => e.CategoryId == catId && e.Language == lang,
                    page,
                    _configuration.ExportPageSize,
                    cancellationToken: cancellationToken);

                foreach (var entry in pagedResult.Items)
                {
                    allEntries.Add(new ExportedEntry
                    {
                        CategoryCode = cat.Code,
                        Key = entry.Key,
                        Text = entry.Text,
                        Pronunciation = entry.Pronunciation,
                        Ruby = entry.Ruby?.ToList(),
                    });
                }

                hasMore = pagedResult.Items.Count == _configuration.ExportPageSize;
                page++;
            }
            while (hasMore);
        }

        var response = new ExportResponse
        {
            Language = body.Language,
            EntryCount = allEntries.Count,
            Entries = allEntries,
        };

        // Cache the compiled bundle with TTL
        var serialized = BannouJson.Serialize(response);
        await _compiledCache.SaveAsync(cacheKey, serialized,
            new StateOptions { Ttl = _configuration.CacheExpirationMinutes * 60 },
            cancellationToken);

        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Compiles pronunciation entries into W3C PLS XML for TTS engines.
    /// Only entries with non-null pronunciation fields are included.
    /// </summary>
    public async Task<(StatusCodes, ExportPlsResponse?)> ExportPlsAsync(ExportPlsRequest body, CancellationToken cancellationToken)
    {
        List<LocalizationCategoryModel> categories;
        if (body.CategoryId != null)
        {
            var category = await _categoryStore.GetAsync(BuildCategoryKey(body.CategoryId.Value), cancellationToken);
            if (category == null) return (StatusCodes.NotFound, null);
            categories = new List<LocalizationCategoryModel> { category };
        }
        else
        {
            var allCategories = await _categoryQueryStore.QueryAsync(c => true, cancellationToken);
            categories = allCategories.ToList();
        }

        var pronunciationEntries = new List<LocalizationEntryModel>();
        foreach (var cat in categories)
        {
            var catId = cat.CategoryId;
            var lang = body.Language;
            var entries = await _entryQueryStore.QueryAsync(
                e => e.CategoryId == catId && e.Language == lang && e.Pronunciation != null,
                cancellationToken);
            pronunciationEntries.AddRange(entries);
        }

        var plsXml = BuildPlsXml(pronunciationEntries, body.Language);

        return (StatusCodes.OK, new ExportPlsResponse
        {
            Language = body.Language,
            EntryCount = pronunciationEntries.Count,
            PlsXml = plsXml,
        });
    }

    #region Private Helpers

    /// <summary>Maps internal category model to API response.</summary>
    private static CategoryResponse MapCategoryResponse(LocalizationCategoryModel model)
    {
        return new CategoryResponse
        {
            CategoryId = model.CategoryId,
            Code = model.Code,
            Description = model.Description,
            IsSchemaDefinition = model.IsSchemaDefinition,
            ValidationMode = model.ValidationMode,
            DefaultLanguage = model.DefaultLanguage,
            EntryCount = model.EntryCount,
            LastEntryUpdateLanguage = model.LastEntryUpdateLanguage,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
        };
    }

    /// <summary>Maps internal entry model to API response.</summary>
    private static EntryResponse MapEntryResponse(LocalizationEntryModel model)
    {
        return new EntryResponse
        {
            EntryId = model.EntryId,
            CategoryId = model.CategoryId,
            Key = model.Key,
            Language = model.Language,
            Text = model.Text,
            Pronunciation = model.Pronunciation,
            Ruby = model.Ruby?.ToList(),
            UpdatedAt = model.UpdatedAt,
        };
    }

    /// <summary>Builds a W3C PLS XML lexicon from pronunciation entries.</summary>
    private static string BuildPlsXml(List<LocalizationEntryModel> entries, string language)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<lexicon version=\"1.0\" xmlns=\"http://www.w3.org/2005/01/pronunciation-lexicon\" alphabet=\"ipa\" xml:lang=\"");
        sb.Append(System.Security.SecurityElement.Escape(language));
        sb.AppendLine("\">");

        foreach (var entry in entries)
        {
            if (entry.Pronunciation == null) continue;
            sb.Append("  <lexeme><grapheme>");
            sb.Append(System.Security.SecurityElement.Escape(entry.Key));
            sb.Append("</grapheme><phoneme>");
            sb.Append(System.Security.SecurityElement.Escape(entry.Pronunciation));
            sb.AppendLine("</phoneme></lexeme>");
        }

        sb.AppendLine("</lexicon>");
        return sb.ToString();
    }

    #endregion
}
