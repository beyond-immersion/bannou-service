using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Localization;

/// <summary>
/// Validates that localization keys exist within categories.
/// Registered as Singleton, discovered by L2+ services via <c>IEnumerable&lt;ILocalizationKeyValidator&gt;</c>.
/// Distributed safety: always safe — reads from MySQL/Redis distributed state.
/// </summary>
public class LocalizationKeyValidator : ILocalizationKeyValidator
{
    private readonly IStateStore<string> _categoryCodeStore;
    private readonly IStateStore<LocalizationCategoryModel> _categoryStore;
    private readonly IStateStore<LocalizationEntryModel> _entryStore;
    private readonly IQueryableStateStore<LocalizationEntryModel> _entryQueryStore;
    private readonly ILogger<LocalizationKeyValidator> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationKeyValidator"/> class.
    /// </summary>
    public LocalizationKeyValidator(
        IStateStoreFactory stateStoreFactory,
        ILogger<LocalizationKeyValidator> logger,
        ITelemetryProvider telemetryProvider)
    {
        _categoryCodeStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.LocalizationCategoryStore);
        _categoryStore = stateStoreFactory.GetStore<LocalizationCategoryModel>(StateStoreDefinitions.LocalizationCategoryStore);
        _entryStore = stateStoreFactory.GetStore<LocalizationEntryModel>(StateStoreDefinitions.LocalizationEntryStore);
        _entryQueryStore = stateStoreFactory.GetQueryableStore<LocalizationEntryModel>(StateStoreDefinitions.LocalizationEntryStore);
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Validates that a localization key (or key prefix) exists in the specified category.
    /// </summary>
    public async ValueTask<bool> ValidateLocalizationKeyAsync(
        string categoryCode,
        string keyPrefix,
        string? keyId,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.localization", "LocalizationKeyValidator.ValidateLocalizationKey");

        var categoryIdStr = await _categoryCodeStore.GetAsync(
            LocalizationService.BuildCategoryCodeKey(categoryCode), ct);
        if (categoryIdStr == null)
            return true; // Category unknown, lenient

        if (!Guid.TryParse(categoryIdStr, out var categoryId))
            return true;

        var category = await _categoryStore.GetAsync(
            LocalizationService.BuildCategoryKey(categoryId), ct);
        if (category == null)
            return true;

        if (category.ValidationMode == ValidationMode.None)
            return true;

        bool exists;
        if (keyId != null)
        {
            var fullKey = $"{keyPrefix}.{keyId}";
            var entry = await _entryStore.GetAsync(
                LocalizationService.BuildEntryKey(categoryId, category.DefaultLanguage, fullKey), ct);
            exists = entry != null;
        }
        else
        {
            var entries = await _entryQueryStore.QueryPagedAsync(
                e => e.CategoryId == categoryId
                    && e.Language == category.DefaultLanguage
                    && e.Key.StartsWith(keyPrefix),
                page: 0,
                pageSize: 1,
                cancellationToken: ct);
            exists = entries.Items.Count > 0;
        }

        if (!exists && category.ValidationMode == ValidationMode.WarnOnMissing)
        {
            _logger.LogWarning(
                "Localization key not found: category {CategoryCode}, prefix {KeyPrefix}, keyId {KeyId}",
                categoryCode, keyPrefix, keyId);
            return true;
        }

        return exists;
    }
}
