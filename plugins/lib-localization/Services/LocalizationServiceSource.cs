// =============================================================================
// Localization Service Source
// Bridges lib-localization (L1) into the ABML behavior system's
// ILocalizationProvider extension point as an ILocalizationSource.
//
// Resolved by FileLocalizationProvider (in lib-behavior) via
// IEnumerable<ILocalizationSource> DI auto-discovery. When both file-based
// and service-backed sources are present, this source's higher Priority (100)
// causes the aggregate to consult the centralized localization tables before
// falling back to embedded YAML files (priority 50).
// =============================================================================

using System.Collections.Concurrent;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Localization;

/// <summary>
/// <see cref="ILocalizationSource"/> implementation backed by lib-localization's
/// <see cref="ILocalizationService"/>. Caches per-locale export bundles in
/// memory and invalidates them via <c>localization.category.updated</c> event
/// subscription for multi-node correctness.
/// </summary>
/// <remarks>
/// <para>
/// <b>Key format</b>: ABML callers use 3-part dotted keys:
/// <c>${localization("items.direwolf.name", "en")}</c>. The first dotted
/// segment is the category code (e.g., <c>items</c>); the remainder is the
/// translation key within that category (e.g., <c>direwolf.name</c>). The
/// service exports yield <see cref="ExportedEntry"/> rows with
/// <see cref="ExportedEntry.CategoryCode"/> and <see cref="ExportedEntry.Key"/>;
/// this source flattens them into a per-locale dictionary keyed by
/// <c>"{categoryCode}.{key}"</c> for direct exact-match lookup at
/// <see cref="GetText"/> time.
/// </para>
/// <para>
/// <b>Caching</b>: Per-locale bundles are loaded lazily on first
/// <see cref="GetText"/> call for a previously-unseen locale, then cached in
/// memory for <see cref="LocalizationServiceConfiguration.CacheExpirationMinutes"/>
/// minutes. Cache invalidation also fires on
/// <c>localization.category.updated</c> events — the cached bundle for the
/// affected language is dropped, forcing a refetch on next access.
/// </para>
/// <para>
/// <b>Distributed safety</b>: Always safe.
/// <see cref="ILocalizationService.ExportLocalizationAsync"/> reads from
/// MySQL/Redis distributed state; cache invalidation fires on the broadcast
/// event, reaching every node.
/// </para>
/// <para>
/// <b>Lifetime</b>: Singleton. Holds the cache and the long-lived event
/// subscription. The scoped <see cref="ILocalizationService"/> is resolved
/// per cache-load via <see cref="IServiceScopeFactory"/>.
/// </para>
/// </remarks>
[BannouHelperService(
    "localization-service-source",
    typeof(ILocalizationService),
    typeof(ILocalizationSource),
    lifetime: ServiceLifetime.Singleton)]
public sealed class LocalizationServiceSource : ILocalizationSource
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LocalizationServiceConfiguration _configuration;
    private readonly ILogger<LocalizationServiceSource> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    // Per-locale bundle cache. Key = BCP 47 locale, value = {categoryCode}.{key} → text.
    private readonly ConcurrentDictionary<string, LocaleBundle> _bundles =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string Name => "localization-service";

    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedLocales =>
        _bundles.Keys.ToList();

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationServiceSource"/> class.
    /// </summary>
    /// <param name="scopeFactory">DI scope factory for resolving the scoped <see cref="ILocalizationService"/> per cache-load.</param>
    /// <param name="configuration">Localization service configuration (provides cache TTL).</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="eventConsumer">Event consumer for cache invalidation on <c>localization.category.updated</c>.</param>
    public LocalizationServiceSource(
        IServiceScopeFactory scopeFactory,
        LocalizationServiceConfiguration configuration,
        ILogger<LocalizationServiceSource> logger,
        ITelemetryProvider telemetryProvider,
        IEventConsumer eventConsumer)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
        _telemetryProvider = telemetryProvider;

        // Cache invalidation: subscribe to BOTH category.updated AND category.deleted.
        //
        // - category.updated fires on entry mutations (SetEntry, DeleteEntry,
        //   BulkSetEntries) carrying LastEntryUpdateLanguage = the affected
        //   language; we invalidate just that language's bundle.
        // - category.deleted fires when a runtime category is removed and
        //   cascades all its entries across ALL languages. The event payload
        //   does not enumerate the affected languages (LastEntryUpdateLanguage
        //   is the most-recent-update sentinel, NOT a complete list), so we
        //   conservatively clear ALL cached bundles — they may contain stale
        //   entries from the deleted category in multiple locales.
        //
        // Reactions MUST clear local state — multi-node distributed delivery
        // via RabbitMQ ensures every node invalidates. Inline invalidation
        // would only reach the processing node.
        eventConsumer.Register<LocalizationCategoryUpdatedEvent>(
            "localization.category.updated",
            $"{nameof(LocalizationServiceSource)}:localization.category.updated",
            async (sp, evt) =>
            {
                await HandleCategoryUpdatedAsync(evt);
            });

        eventConsumer.Register<LocalizationCategoryDeletedEvent>(
            "localization.category.deleted",
            $"{nameof(LocalizationServiceSource)}:localization.category.deleted",
            async (sp, evt) =>
            {
                await HandleCategoryDeletedAsync(evt);
            });
    }

    /// <inheritdoc />
    public string? GetText(string key, string locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(locale);

        // Cache hit (and not stale)?
        if (_bundles.TryGetValue(locale, out var bundle))
        {
            if (DateTimeOffset.UtcNow - bundle.LoadedAt
                > TimeSpan.FromMinutes(_configuration.CacheExpirationMinutes))
            {
                _bundles.TryRemove(locale, out _);
            }
            else
            {
                return bundle.Entries.TryGetValue(key, out var text) ? text : null;
            }
        }

        // First-load (or post-expiry refetch) sync-block.
        // Task.Run avoids capturing the caller's SynchronizationContext, which
        // makes sync-over-async safe across ASP.NET Core / embedded modes.
        // The GetText interface is sync to match the established
        // ILocalizationSource hot-path contract (mirroring YamlFileLocalizationSource).
        var loaded = Task.Run(() => LoadBundleAsync(locale, CancellationToken.None))
            .GetAwaiter()
            .GetResult();

        if (loaded != null)
        {
            _bundles[locale] = loaded;
            return loaded.Entries.TryGetValue(key, out var t) ? t : null;
        }

        return null;
    }

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.localization", "LocalizationServiceSource.ReloadAsync");

        // Drop all cached bundles. Next GetText for each locale re-fetches.
        // No pre-warming — we don't know which locales callers will request.
        _bundles.Clear();
        await Task.CompletedTask;
    }

    private async Task<LocaleBundle?> LoadBundleAsync(string locale, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.localization", "LocalizationServiceSource.LoadBundle");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ILocalizationService>();

            var (status, response) = await service.ExportLocalizationAsync(
                new ExportRequest { Language = locale }, ct);

            if (status != StatusCodes.OK || response == null)
            {
                _logger.LogDebug(
                    "ExportLocalization returned {Status} for locale {Locale}",
                    status, locale);
                return null;
            }

            var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in response.Entries)
            {
                // 3-part dotted key form: "{categoryCode}.{entry.Key}"
                // ABML caller uses ${localization("items.direwolf.name", "en")}
                // which exact-matches the dictionary key "items.direwolf.name".
                entries[$"{entry.CategoryCode}.{entry.Key}"] = entry.Text;
            }

            return new LocaleBundle(entries, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load localization bundle for locale {Locale}", locale);
            return null;
        }
    }

    private async Task HandleCategoryUpdatedAsync(LocalizationCategoryUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.localization", "LocalizationServiceSource.HandleCategoryUpdated");

        // Invalidate the cached bundle for the affected language only.
        // Other locales remain valid — they don't include this language's
        // entries for the changed category.
        if (!string.IsNullOrEmpty(evt.LastEntryUpdateLanguage))
        {
            if (_bundles.TryRemove(evt.LastEntryUpdateLanguage, out _))
            {
                _logger.LogDebug(
                    "Invalidated localization bundle for locale {Locale} after category {Code} update",
                    evt.LastEntryUpdateLanguage, evt.Code);
            }
        }
        else
        {
            // No language-specific change reported (e.g., category metadata
            // change with no entry mutation). Conservative: invalidate all.
            _bundles.Clear();
            _logger.LogDebug(
                "Invalidated all localization bundles after non-language-scoped category {Code} update",
                evt.Code);
        }

        await Task.CompletedTask;
    }

    private async Task HandleCategoryDeletedAsync(LocalizationCategoryDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.localization", "LocalizationServiceSource.HandleCategoryDeleted");

        // Category deletion cascades all entries across every language they
        // were authored in. The event does not enumerate the affected
        // languages (LastEntryUpdateLanguage is a most-recent-update sentinel,
        // NOT a complete list), so we have to invalidate every cached bundle
        // to avoid serving stale entries from the deleted category.
        var clearedCount = _bundles.Count;
        _bundles.Clear();
        _logger.LogDebug(
            "Invalidated all localization bundles ({Count} cached locales) after category {Code} deletion",
            clearedCount, evt.Code);

        await Task.CompletedTask;
    }

    private sealed record LocaleBundle(
        Dictionary<string, string> Entries,
        DateTimeOffset LoadedAt);
}
