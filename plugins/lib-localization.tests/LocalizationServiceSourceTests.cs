// =============================================================================
// LocalizationServiceSource Tests
// Validates the lib-localization → ILocalizationProvider bridge:
//  - Per-locale bundle caching with TTL refetch
//  - Cache invalidation via localization.category.updated events
//  - 3-part dotted key resolution ({categoryCode}.{key})
//  - ReloadAsync invalidate-only semantics
// See: docs/reference/tenets/TESTING-PATTERNS.md (Capture Pattern)
// =============================================================================

using System.Linq.Expressions;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Localization;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeyondImmersion.BannouService.Localization.Tests;

/// <summary>
/// Constructor validation per project convention.
/// </summary>
public class LocalizationServiceSourceConstructorTests
{
    [Fact]
    public void LocalizationServiceSource_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<LocalizationServiceSource>();
}

/// <summary>
/// Shared fixture mocking IServiceScopeFactory + ILocalizationService and capturing
/// the IEventConsumer-registered handler so tests can fire it synthetically.
/// </summary>
public abstract class LocalizationServiceSourceTestBase
{
    protected readonly Mock<IServiceScopeFactory> MockScopeFactory = new();
    protected readonly Mock<IServiceScope> MockScope = new();
    protected readonly Mock<IServiceProvider> MockScopeServiceProvider = new();
    protected readonly Mock<ILocalizationService> MockLocalizationService = new();
    protected readonly Mock<IEventConsumer> MockEventConsumer = new();
    protected readonly Mock<ITelemetryProvider> MockTelemetryProvider = new();
    protected readonly LocalizationServiceConfiguration Configuration;
    protected readonly LocalizationServiceSource Source;

    /// <summary>
    /// The handler the source registered for the localization.category.updated
    /// topic at construction time, captured via Moq.Callback. Tests can invoke
    /// it to simulate an event arriving from the broker.
    /// </summary>
    protected Func<IServiceProvider, LocalizationCategoryUpdatedEvent, Task>? CapturedHandler;

    /// <summary>
    /// The handler the source registered for the localization.category.deleted
    /// topic at construction time, captured via Moq.Callback.
    /// </summary>
    protected Func<IServiceProvider, LocalizationCategoryDeletedEvent, Task>? CapturedDeletedHandler;

    protected LocalizationServiceSourceTestBase()
    {
        Configuration = new LocalizationServiceConfiguration
        {
            DefaultLanguage = "en",
            DefaultValidationMode = ValidationMode.None,
            MaxEntriesPerCategory = 10000,
            LockExpirySeconds = 15,
            // Long TTL so cache-hit tests don't accidentally hit the staleness path.
            CacheExpirationMinutes = 60,
            ExportPageSize = 5000,
        };

        // Wire IServiceScopeFactory → IServiceScope → IServiceProvider →
        // ILocalizationService for in-source GetRequiredService<ILocalizationService>().
        MockScopeFactory.Setup(f => f.CreateScope()).Returns(MockScope.Object);
        MockScope.Setup(s => s.ServiceProvider).Returns(MockScopeServiceProvider.Object);
        MockScopeServiceProvider.Setup(sp => sp.GetService(typeof(ILocalizationService)))
            .Returns(MockLocalizationService.Object);

        // Capture both registered handlers (updated + deleted) so tests can fire
        // events synthetically. The source registers two subscriptions in its
        // constructor — one per event type.
        MockEventConsumer.Setup(c => c.Register(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<IServiceProvider, LocalizationCategoryUpdatedEvent, Task>>()))
            .Callback<string, string, Func<IServiceProvider, LocalizationCategoryUpdatedEvent, Task>>(
                (_, _, handler) => CapturedHandler = handler);

        MockEventConsumer.Setup(c => c.Register(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<IServiceProvider, LocalizationCategoryDeletedEvent, Task>>()))
            .Callback<string, string, Func<IServiceProvider, LocalizationCategoryDeletedEvent, Task>>(
                (_, _, handler) => CapturedDeletedHandler = handler);

        Source = new LocalizationServiceSource(
            MockScopeFactory.Object,
            Configuration,
            NullLogger<LocalizationServiceSource>.Instance,
            MockTelemetryProvider.Object,
            MockEventConsumer.Object);
    }

    /// <summary>
    /// Sets up <see cref="ILocalizationService.ExportLocalizationAsync"/> for a
    /// given locale to return the supplied entries. Each entry produces an
    /// <c>{categoryCode}.{key}</c> lookup key in the source's cached bundle.
    /// </summary>
    protected void SetupExport(string locale, params (string categoryCode, string key, string text)[] entries)
    {
        var response = new ExportResponse
        {
            Language = locale,
            EntryCount = entries.Length,
            Entries = entries
                .Select(e => new ExportedEntry
                {
                    CategoryCode = e.categoryCode,
                    Key = e.key,
                    Text = e.text,
                })
                .ToList(),
        };

        MockLocalizationService
            .Setup(s => s.ExportLocalizationAsync(
                It.Is<ExportRequest>(r => r.Language == locale),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.OK, response));
    }

    /// <summary>
    /// Sets up <see cref="ILocalizationService.ExportLocalizationAsync"/> for a
    /// given locale to return NotFound (no bundle available).
    /// </summary>
    protected void SetupExportNotFound(string locale)
    {
        MockLocalizationService
            .Setup(s => s.ExportLocalizationAsync(
                It.Is<ExportRequest>(r => r.Language == locale),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.NotFound, (ExportResponse?)null));
    }
}

/// <summary>
/// Static identity (Name + Priority) tests.
/// </summary>
public class LocalizationServiceSourceIdentityTests : LocalizationServiceSourceTestBase
{
    [Fact]
    public void Name_ReturnsLocalizationService()
    {
        Assert.Equal("localization-service", Source.Name);
    }

    [Fact]
    public void Priority_Returns100_HigherThanFileSource()
    {
        Assert.Equal(100, Source.Priority);
    }
}

/// <summary>
/// GetText behavior tests — input validation, cache miss/hit, key shape.
/// </summary>
public class LocalizationServiceSourceGetTextTests : LocalizationServiceSourceTestBase
{
    [Fact]
    public void GetText_LocaleNotCached_FetchesViaServiceAndCaches()
    {
        SetupExport("en", ("items", "direwolf.name", "Direwolf"));

        // First call — should fetch
        var first = Source.GetText("items.direwolf.name", "en");

        // Second call — should hit cache (no second fetch)
        var second = Source.GetText("items.direwolf.name", "en");

        Assert.Equal("Direwolf", first);
        Assert.Equal("Direwolf", second);

        // Verify exactly one fetch happened — second call hit the cache.
        MockLocalizationService.Verify(s => s.ExportLocalizationAsync(
            It.Is<ExportRequest>(r => r.Language == "en"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GetText_3PartDottedKey_ResolvesCategoryAndKey()
    {
        SetupExport("en",
            ("items", "direwolf.name", "Direwolf"),
            ("items", "direwolf.description", "A massive wolf-like creature."),
            ("quests", "rescue-princess.title", "Rescue the Princess"));

        Assert.Equal("Direwolf", Source.GetText("items.direwolf.name", "en"));
        Assert.Equal("A massive wolf-like creature.",
            Source.GetText("items.direwolf.description", "en"));
        Assert.Equal("Rescue the Princess",
            Source.GetText("quests.rescue-princess.title", "en"));
    }

    [Fact]
    public void GetText_MissingKey_ReturnsNull()
    {
        SetupExport("en", ("items", "direwolf.name", "Direwolf"));

        var missing = Source.GetText("items.does-not-exist", "en");

        Assert.Null(missing);
    }

    [Fact]
    public void GetText_LocaleNotInService_ReturnsNull()
    {
        SetupExportNotFound("ja");

        var notFound = Source.GetText("items.direwolf.name", "ja");

        Assert.Null(notFound);
    }

    [Fact]
    public void GetText_EmptyKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Source.GetText(string.Empty, "en"));
    }

    [Fact]
    public void GetText_EmptyLocale_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Source.GetText("items.direwolf.name", string.Empty));
    }

    [Fact]
    public void GetText_DifferentLocales_LoadIndependently()
    {
        SetupExport("en", ("items", "direwolf.name", "Direwolf"));
        SetupExport("fr", ("items", "direwolf.name", "Loup-garou"));

        var en = Source.GetText("items.direwolf.name", "en");
        var fr = Source.GetText("items.direwolf.name", "fr");

        Assert.Equal("Direwolf", en);
        Assert.Equal("Loup-garou", fr);

        // Each locale fetched exactly once
        MockLocalizationService.Verify(s => s.ExportLocalizationAsync(
            It.IsAny<ExportRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}

/// <summary>
/// Cache invalidation via localization.category.updated event subscription.
/// </summary>
public class LocalizationServiceSourceInvalidationTests : LocalizationServiceSourceTestBase
{
    [Fact]
    public void Constructor_RegistersHandlerForCategoryUpdatedTopic()
    {
        // Capture happened in the base fixture's constructor.
        Assert.NotNull(CapturedHandler);

        MockEventConsumer.Verify(c => c.Register(
            "localization.category.updated",
            It.IsAny<string>(),
            It.IsAny<Func<IServiceProvider, LocalizationCategoryUpdatedEvent, Task>>()),
            Times.Once);
    }

    [Fact]
    public async Task CategoryUpdated_InvalidatesAffectedLanguageOnly()
    {
        SetupExport("en", ("items", "key1", "English"));
        SetupExport("fr", ("items", "key1", "French"));

        // Pre-load both bundles
        Source.GetText("items.key1", "en");
        Source.GetText("items.key1", "fr");

        // Confirm both bundles are cached (each fetched exactly once so far)
        MockLocalizationService.Verify(s => s.ExportLocalizationAsync(
            It.IsAny<ExportRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Fire localization.category.updated for "en" only
        Assert.NotNull(CapturedHandler);
        await CapturedHandler!(MockScopeServiceProvider.Object, new LocalizationCategoryUpdatedEvent
        {
            CategoryId = Guid.NewGuid(),
            Code = "items",
            Description = "Items",
            IsSchemaDefinition = false,
            ValidationMode = ValidationMode.None,
            DefaultLanguage = "en",
            EntryCount = 5,
            LastEntryUpdateLanguage = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ChangedFields = new List<string> { "entries" },
        });

        // Re-query both — en should refetch, fr should remain cached
        Source.GetText("items.key1", "en");
        Source.GetText("items.key1", "fr");

        MockLocalizationService.Verify(s => s.ExportLocalizationAsync(
            It.Is<ExportRequest>(r => r.Language == "en"),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        MockLocalizationService.Verify(s => s.ExportLocalizationAsync(
            It.Is<ExportRequest>(r => r.Language == "fr"),
            It.IsAny<CancellationToken>()), Times.Exactly(1));
    }

    [Fact]
    public async Task CategoryUpdated_NoLanguageInEvent_InvalidatesAllBundles()
    {
        SetupExport("en", ("items", "key1", "English"));
        SetupExport("fr", ("items", "key1", "French"));

        Source.GetText("items.key1", "en");
        Source.GetText("items.key1", "fr");

        Assert.NotNull(CapturedHandler);
        await CapturedHandler!(MockScopeServiceProvider.Object, new LocalizationCategoryUpdatedEvent
        {
            CategoryId = Guid.NewGuid(),
            Code = "items",
            Description = "Items",
            IsSchemaDefinition = false,
            ValidationMode = ValidationMode.None,
            DefaultLanguage = "en",
            EntryCount = 5,
            // LastEntryUpdateLanguage omitted — non-language-scoped change
            LastEntryUpdateLanguage = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ChangedFields = new List<string> { "description" },
        });

        // Both should refetch
        Source.GetText("items.key1", "en");
        Source.GetText("items.key1", "fr");

        MockLocalizationService.Verify(s => s.ExportLocalizationAsync(
            It.Is<ExportRequest>(r => r.Language == "en"),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        MockLocalizationService.Verify(s => s.ExportLocalizationAsync(
            It.Is<ExportRequest>(r => r.Language == "fr"),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void Constructor_RegistersHandlerForCategoryDeletedTopic()
    {
        // Capture happened in the base fixture's constructor.
        Assert.NotNull(CapturedDeletedHandler);

        MockEventConsumer.Verify(c => c.Register(
            "localization.category.deleted",
            It.IsAny<string>(),
            It.IsAny<Func<IServiceProvider, LocalizationCategoryDeletedEvent, Task>>()),
            Times.Once);
    }

    [Fact]
    public async Task CategoryDeleted_InvalidatesAllCachedBundles()
    {
        // A runtime category deletion cascades all its entries across all
        // languages. The event payload doesn't enumerate the affected
        // languages, so the source must conservatively clear every cached
        // bundle to avoid serving stale entries.
        SetupExport("en", ("items", "key1", "English"));
        SetupExport("fr", ("items", "key1", "French"));
        SetupExport("ja", ("items", "key1", "Japanese"));

        // Pre-load three bundles
        Source.GetText("items.key1", "en");
        Source.GetText("items.key1", "fr");
        Source.GetText("items.key1", "ja");

        MockLocalizationService.Verify(s => s.ExportLocalizationAsync(
            It.IsAny<ExportRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));

        // Fire localization.category.deleted
        Assert.NotNull(CapturedDeletedHandler);
        await CapturedDeletedHandler!(MockScopeServiceProvider.Object, new LocalizationCategoryDeletedEvent
        {
            CategoryId = Guid.NewGuid(),
            Code = "items",
            Description = "Items",
            IsSchemaDefinition = false,
            ValidationMode = ValidationMode.None,
            DefaultLanguage = "en",
            EntryCount = 0,
            LastEntryUpdateLanguage = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        // Every bundle should refetch on next access — this is the bug fix:
        // before this subscription was added, only the TTL (default 60min)
        // would eventually evict the stale entries.
        Source.GetText("items.key1", "en");
        Source.GetText("items.key1", "fr");
        Source.GetText("items.key1", "ja");

        MockLocalizationService.Verify(s => s.ExportLocalizationAsync(
            It.IsAny<ExportRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(6));
    }
}

/// <summary>
/// ReloadAsync, SupportedLocales, and TTL-driven refetch.
/// </summary>
public class LocalizationServiceSourceLifecycleTests : LocalizationServiceSourceTestBase
{
    [Fact]
    public async Task ReloadAsync_ClearsAllCachedBundles_NextGetTextRefetches()
    {
        SetupExport("en", ("items", "key1", "Hello"));

        Source.GetText("items.key1", "en");

        await Source.ReloadAsync(TestContext.Current.CancellationToken);

        Source.GetText("items.key1", "en");

        // Two fetches: initial + post-reload.
        MockLocalizationService.Verify(s => s.ExportLocalizationAsync(
            It.IsAny<ExportRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void SupportedLocales_ReflectsCachedBundleKeys()
    {
        SetupExport("en", ("items", "key1", "English"));
        SetupExport("fr", ("items", "key1", "French"));

        // Initially empty (nothing cached)
        Assert.Empty(Source.SupportedLocales);

        Source.GetText("items.key1", "en");
        Source.GetText("items.key1", "fr");

        Assert.Contains("en", Source.SupportedLocales);
        Assert.Contains("fr", Source.SupportedLocales);
    }

    [Fact]
    public void GetText_StaleCachedBundle_RefetchesFromService()
    {
        // TTL=0 forces every call to be considered stale.
        Configuration.CacheExpirationMinutes = 0;
        SetupExport("en", ("items", "key1", "Hello"));

        Source.GetText("items.key1", "en");
        Source.GetText("items.key1", "en");
        Source.GetText("items.key1", "en");

        // Three calls, each refetching because cache is always stale.
        MockLocalizationService.Verify(s => s.ExportLocalizationAsync(
            It.IsAny<ExportRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
}
