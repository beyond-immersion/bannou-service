using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Localization;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq.Expressions;

namespace BeyondImmersion.BannouService.Localization.Tests;

/// <summary>
/// Unit tests for LocalizationService derived from the implementation map pseudocode.
/// Tests use the Capture Pattern for state saves and event publications.
/// See: docs/maps/LOCALIZATION.md, docs/reference/tenets/TESTING-PATTERNS.md
/// </summary>
public class LocalizationServiceConstructorTests
{
    [Fact]
    public void LocalizationService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<LocalizationService>();
}

#region Test Fixture Base

/// <summary>
/// Shared test fixture providing mocked dependencies and capture infrastructure.
/// </summary>
public abstract class LocalizationServiceTestBase
{
    protected readonly Mock<IStateStore<LocalizationCategoryModel>> MockCategoryStore = new();
    protected readonly Mock<IQueryableStateStore<LocalizationCategoryModel>> MockCategoryQueryStore = new();
    protected readonly Mock<IStateStore<string>> MockCategoryCodeStore = new();
    protected readonly Mock<IStateStore<LocalizationEntryModel>> MockEntryStore = new();
    protected readonly Mock<IQueryableStateStore<LocalizationEntryModel>> MockEntryQueryStore = new();
    protected readonly Mock<IStateStore<string>> MockCompiledCache = new();
    protected readonly Mock<IDistributedLockProvider> MockLockProvider = new();
    protected readonly Mock<IMessageBus> MockMessageBus = new();
    protected readonly Mock<IEventConsumer> MockEventConsumer = new();
    protected readonly Mock<ITelemetryProvider> MockTelemetryProvider = new();
    protected readonly LocalizationServiceConfiguration Configuration;
    protected readonly LocalizationService Service;

    // Capture fields
    protected string? CapturedTopic;
    protected object? CapturedEvent;
    protected readonly List<(string Key, object Value)> CapturedSaves = new();

    protected LocalizationServiceTestBase()
    {
        Configuration = new LocalizationServiceConfiguration
        {
            DefaultLanguage = "en",
            DefaultValidationMode = ValidationMode.None,
            MaxEntriesPerCategory = 10000,
            LockExpirySeconds = 15,
            CacheExpirationMinutes = 60,
            ExportPageSize = 5000,
        };

        var mockFactory = new Mock<IStateStoreFactory>();
        mockFactory.Setup(f => f.GetStore<LocalizationCategoryModel>(StateStoreDefinitions.LocalizationCategoryStore))
            .Returns(MockCategoryStore.Object);
        mockFactory.Setup(f => f.GetQueryableStore<LocalizationCategoryModel>(StateStoreDefinitions.LocalizationCategoryStore))
            .Returns(MockCategoryQueryStore.Object);
        // The code store uses the same store name but typed as string
        mockFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.LocalizationCategoryStore))
            .Returns(MockCategoryCodeStore.Object);
        mockFactory.Setup(f => f.GetStore<LocalizationEntryModel>(StateStoreDefinitions.LocalizationEntryStore))
            .Returns(MockEntryStore.Object);
        mockFactory.Setup(f => f.GetQueryableStore<LocalizationEntryModel>(StateStoreDefinitions.LocalizationEntryStore))
            .Returns(MockEntryQueryStore.Object);
        mockFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.LocalizationCompiledCache))
            .Returns(MockCompiledCache.Object);

        // Default lock success
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        MockLockProvider.Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Default message bus capture
        MockMessageBus.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                CapturedTopic = topic;
                CapturedEvent = evt;
            })
            .ReturnsAsync(true);

        // Default save returns valid etag
        MockCategoryStore.Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<LocalizationCategoryModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        MockCategoryCodeStore.Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        MockEntryStore.Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<LocalizationEntryModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        MockCompiledCache.Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        Service = new LocalizationService(
            mockFactory.Object,
            MockLockProvider.Object,
            MockMessageBus.Object,
            new Mock<ILogger<LocalizationService>>().Object,
            Configuration,
            MockTelemetryProvider.Object,
            MockEventConsumer.Object);
    }

    protected LocalizationCategoryModel CreateTestCategory(
        Guid? categoryId = null,
        string code = "test-category",
        bool isSchemaDefinition = false,
        int entryCount = 0)
    {
        return new LocalizationCategoryModel
        {
            CategoryId = categoryId ?? Guid.NewGuid(),
            Code = code,
            Description = "Test category",
            IsSchemaDefinition = isSchemaDefinition,
            ValidationMode = ValidationMode.None,
            DefaultLanguage = "en",
            EntryCount = entryCount,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    protected LocalizationEntryModel CreateTestEntry(
        Guid categoryId,
        string key = "test.key",
        string language = "en",
        string text = "Test text")
    {
        return new LocalizationEntryModel
        {
            EntryId = Guid.NewGuid(),
            CategoryId = categoryId,
            Key = key,
            Language = language,
            Text = text,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    protected void SetupCategoryExists(LocalizationCategoryModel model)
    {
        MockCategoryStore.Setup(s => s.GetAsync(
                LocalizationService.BuildCategoryKey(model.CategoryId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        MockCategoryStore.Setup(s => s.GetWithETagAsync(
                LocalizationService.BuildCategoryKey(model.CategoryId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        MockCategoryCodeStore.Setup(s => s.GetAsync(
                LocalizationService.BuildCategoryCodeKey(model.Code), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model.CategoryId.ToString());

        MockCategoryStore.Setup(s => s.TrySaveAsync(
                It.IsAny<string>(), It.IsAny<LocalizationCategoryModel>(), It.IsAny<string>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");
    }
}

#endregion

/// <summary>
/// Category endpoint tests derived from implementation map § Methods.
/// </summary>
public class LocalizationServiceCategoryTests : LocalizationServiceTestBase
{
    [Fact]
    public async Task CreateCategoryAsync_ValidRequest_ReturnsOkWithCategoryId()
    {
        var request = new CreateCategoryRequest
        {
            Code = "new-category",
            Description = "A new category",
        };

        var (status, response) = await Service.CreateCategoryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.CategoryId);
        Assert.Equal("new-category", response.Code);
        Assert.Equal(ValidationMode.None, response.ValidationMode);
        Assert.Equal("en", response.DefaultLanguage);
        Assert.Equal(0, response.EntryCount);

        // Verify event published
        Assert.NotNull(CapturedEvent);
        var createdEvent = Assert.IsType<LocalizationCategoryCreatedEvent>(CapturedEvent);
        Assert.Equal("new-category", createdEvent.Code);
        Assert.Equal(response.CategoryId, createdEvent.CategoryId);
    }

    [Fact]
    public async Task CreateCategoryAsync_DuplicateCode_ReturnsConflict()
    {
        MockCategoryCodeStore.Setup(s => s.GetAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing-id");

        var request = new CreateCategoryRequest
        {
            Code = "existing",
            Description = "Duplicate",
        };

        var (status, response) = await Service.CreateCategoryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        // No save or publish should have occurred
        MockCategoryStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<LocalizationCategoryModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCategoryAsync_ByCategoryId_ReturnsCategory()
    {
        var category = CreateTestCategory();
        SetupCategoryExists(category);

        var request = new GetCategoryRequest { CategoryId = category.CategoryId };

        var (status, response) = await Service.GetCategoryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(category.CategoryId, response.CategoryId);
        Assert.Equal(category.Code, response.Code);
    }

    [Fact]
    public async Task GetCategoryAsync_ByCode_ReturnsCategory()
    {
        var category = CreateTestCategory();
        SetupCategoryExists(category);

        var request = new GetCategoryRequest { Code = category.Code };

        var (status, response) = await Service.GetCategoryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(category.CategoryId, response.CategoryId);
    }

    [Fact]
    public async Task GetCategoryAsync_NeitherIdNorCode_ReturnsBadRequest()
    {
        var request = new GetCategoryRequest();

        var (status, response) = await Service.GetCategoryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetCategoryAsync_NotFound_ReturnsNotFound()
    {
        var request = new GetCategoryRequest { CategoryId = Guid.NewGuid() };

        var (status, response) = await Service.GetCategoryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateCategoryAsync_ValidRequest_ReturnsUpdatedCategory()
    {
        var category = CreateTestCategory();
        SetupCategoryExists(category);

        var request = new UpdateCategoryRequest
        {
            CategoryId = category.CategoryId,
            Description = "Updated description",
        };

        var (status, response) = await Service.UpdateCategoryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated description", response.Description);

        // Verify event published with changedFields
        Assert.NotNull(CapturedEvent);
        var updatedEvent = Assert.IsType<LocalizationCategoryUpdatedEvent>(CapturedEvent);
        Assert.Contains("description", updatedEvent.ChangedFields);
    }

    [Fact]
    public async Task UpdateCategoryAsync_NotFound_ReturnsNotFound()
    {
        var request = new UpdateCategoryRequest
        {
            CategoryId = Guid.NewGuid(),
            Description = "Updated",
        };

        var (status, response) = await Service.UpdateCategoryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteCategoryAsync_RuntimeCategory_DeletesAndPublishes()
    {
        var category = CreateTestCategory(isSchemaDefinition: false, entryCount: 2);
        SetupCategoryExists(category);

        // Setup entry query to return entries for cascade
        MockEntryQueryStore.Setup(q => q.QueryAsync(
                It.IsAny<Expression<Func<LocalizationEntryModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LocalizationEntryModel>
            {
                CreateTestEntry(category.CategoryId, "key1", "en"),
                CreateTestEntry(category.CategoryId, "key2", "en"),
            });

        var request = new DeleteCategoryRequest { CategoryId = category.CategoryId };

        var (status, response) = await Service.DeleteCategoryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify category and entries were deleted
        MockCategoryStore.Verify(s => s.DeleteAsync(
            LocalizationService.BuildCategoryKey(category.CategoryId), It.IsAny<CancellationToken>()), Times.Once);
        MockEntryStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Verify event published
        Assert.NotNull(CapturedEvent);
        var deletedEvent = Assert.IsType<LocalizationCategoryDeletedEvent>(CapturedEvent);
        Assert.Equal(category.CategoryId, deletedEvent.CategoryId);
    }

    [Fact]
    public async Task DeleteCategoryAsync_SchemaDefinedCategory_ReturnsBadRequest()
    {
        var category = CreateTestCategory(isSchemaDefinition: true);
        SetupCategoryExists(category);

        var request = new DeleteCategoryRequest { CategoryId = category.CategoryId };

        var (status, response) = await Service.DeleteCategoryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);

        // Verify no delete occurred
        MockCategoryStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteCategoryAsync_NotFound_ReturnsNotFound()
    {
        var request = new DeleteCategoryRequest { CategoryId = Guid.NewGuid() };

        var (status, response) = await Service.DeleteCategoryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }
}

/// <summary>
/// Entry endpoint tests derived from implementation map § Methods.
/// </summary>
public class LocalizationServiceEntryTests : LocalizationServiceTestBase
{
    [Fact]
    public async Task SetEntryAsync_NewEntry_CreatesAndPublishesUpdate()
    {
        var category = CreateTestCategory(entryCount: 5);
        SetupCategoryExists(category);

        var request = new SetEntryRequest
        {
            CategoryId = category.CategoryId,
            Key = "greeting.hello",
            Language = "en",
            Text = "Hello!",
        };

        var (status, response) = await Service.SetEntryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.EntryId);
        Assert.Equal("greeting.hello", response.Key);
        Assert.Equal("Hello!", response.Text);

        // Verify entry saved
        MockEntryStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<LocalizationEntryModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published with changedFields: ["entries"]
        Assert.NotNull(CapturedEvent);
        var updatedEvent = Assert.IsType<LocalizationCategoryUpdatedEvent>(CapturedEvent);
        Assert.Contains("entries", updatedEvent.ChangedFields);
    }

    [Fact]
    public async Task SetEntryAsync_ExistingEntry_UpdatesWithoutCountIncrement()
    {
        var category = CreateTestCategory(entryCount: 5);
        SetupCategoryExists(category);

        var existingEntry = CreateTestEntry(category.CategoryId, "greeting.hello", "en", "Old text");
        MockEntryStore.Setup(s => s.GetAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        var request = new SetEntryRequest
        {
            CategoryId = category.CategoryId,
            Key = "greeting.hello",
            Language = "en",
            Text = "New text",
        };

        var (status, response) = await Service.SetEntryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(existingEntry.EntryId, response.EntryId); // Reuses existing ID

        // Should NOT have called GetWithETagAsync for count update (not new)
        MockCategoryStore.Verify(s => s.GetWithETagAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetEntryAsync_CategoryNotFound_ReturnsNotFound()
    {
        var request = new SetEntryRequest
        {
            CategoryId = Guid.NewGuid(),
            Key = "test",
            Language = "en",
            Text = "test",
        };

        var (status, response) = await Service.SetEntryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SetEntryAsync_MaxEntriesExceeded_ReturnsConflict()
    {
        var category = CreateTestCategory(entryCount: 10000);
        SetupCategoryExists(category);
        Configuration.MaxEntriesPerCategory = 10000;

        var request = new SetEntryRequest
        {
            CategoryId = category.CategoryId,
            Key = "new.key",
            Language = "en",
            Text = "text",
        };

        var (status, response) = await Service.SetEntryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetEntryAsync_ExistingEntry_ReturnsEntry()
    {
        var category = CreateTestCategory();
        SetupCategoryExists(category);

        var entry = CreateTestEntry(category.CategoryId, "test.key", "en", "Hello");
        entry.Pronunciation = "həˈloʊ";
        MockEntryStore.Setup(s => s.GetAsync(
                LocalizationService.BuildEntryKey(category.CategoryId, "en", "test.key"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var request = new GetEntryRequest
        {
            CategoryId = category.CategoryId,
            Key = "test.key",
            Language = "en",
        };

        var (status, response) = await Service.GetEntryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Hello", response.Text);
        Assert.Equal("həˈloʊ", response.Pronunciation);
    }

    [Fact]
    public async Task GetEntryAsync_EntryNotFound_ReturnsNotFound()
    {
        var category = CreateTestCategory();
        SetupCategoryExists(category);

        var request = new GetEntryRequest
        {
            CategoryId = category.CategoryId,
            Key = "nonexistent",
            Language = "en",
        };

        var (status, response) = await Service.GetEntryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteEntryAsync_ExistingEntry_DeletesAndPublishes()
    {
        var category = CreateTestCategory(entryCount: 3);
        SetupCategoryExists(category);

        var entry = CreateTestEntry(category.CategoryId, "to-delete", "en");
        MockEntryStore.Setup(s => s.GetAsync(
                LocalizationService.BuildEntryKey(category.CategoryId, "en", "to-delete"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var request = new DeleteEntryRequest
        {
            CategoryId = category.CategoryId,
            Key = "to-delete",
            Language = "en",
        };

        var (status, response) = await Service.DeleteEntryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify entry deleted
        MockEntryStore.Verify(s => s.DeleteAsync(
            LocalizationService.BuildEntryKey(category.CategoryId, "en", "to-delete"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published
        Assert.NotNull(CapturedEvent);
        var updatedEvent = Assert.IsType<LocalizationCategoryUpdatedEvent>(CapturedEvent);
        Assert.Contains("entries", updatedEvent.ChangedFields);
    }

    [Fact]
    public async Task DeleteEntryAsync_EntryNotFound_ReturnsNotFound()
    {
        var category = CreateTestCategory();
        SetupCategoryExists(category);

        var request = new DeleteEntryRequest
        {
            CategoryId = category.CategoryId,
            Key = "nonexistent",
            Language = "en",
        };

        var (status, response) = await Service.DeleteEntryAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BulkSetEntriesAsync_ValidBatch_ReturnsSuccessCount()
    {
        var category = CreateTestCategory(entryCount: 0);
        SetupCategoryExists(category);

        var request = new BulkSetEntriesRequest
        {
            CategoryId = category.CategoryId,
            Language = "en",
            Entries = new List<BulkEntryItem>
            {
                new BulkEntryItem { Key = "key1", Text = "Text 1" },
                new BulkEntryItem { Key = "key2", Text = "Text 2" },
            },
        };

        var (status, response) = await Service.BulkSetEntriesAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.SucceededCount);
        Assert.Equal(0, response.FailedCount);

        // Verify single event published (not per-entry)
        MockMessageBus.Verify(m => m.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkSetEntriesAsync_CategoryNotFound_ReturnsNotFound()
    {
        var request = new BulkSetEntriesRequest
        {
            CategoryId = Guid.NewGuid(),
            Language = "en",
            Entries = new List<BulkEntryItem>
            {
                new BulkEntryItem { Key = "key1", Text = "Text 1" },
            },
        };

        var (status, response) = await Service.BulkSetEntriesAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }
}

/// <summary>
/// Export endpoint tests derived from implementation map § Methods.
/// </summary>
public class LocalizationServiceExportTests : LocalizationServiceTestBase
{
    [Fact]
    public async Task ExportLocalizationAsync_CacheHit_ReturnsCachedBundle()
    {
        var cachedJson = BeyondImmersion.Bannou.Core.BannouJson.Serialize(new ExportResponse
        {
            Language = "en",
            EntryCount = 1,
            Entries = new List<ExportedEntry>
            {
                new ExportedEntry { CategoryCode = "items", Key = "sword.name", Text = "Sword" },
            },
        });

        MockCompiledCache.Setup(s => s.GetAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedJson);

        var request = new ExportRequest { Language = "en" };

        var (status, response) = await Service.ExportLocalizationAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.EntryCount);

        // Verify no MySQL query was performed
        MockCategoryQueryStore.Verify(q => q.QueryAsync(
            It.IsAny<Expression<Func<LocalizationCategoryModel, bool>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExportLocalizationAsync_CacheMiss_CompilesAndCaches()
    {
        var category = CreateTestCategory(code: "items");
        var entry = CreateTestEntry(category.CategoryId, "sword.name", "en", "Sword");

        MockCategoryQueryStore.Setup(q => q.QueryAsync(
                It.IsAny<Expression<Func<LocalizationCategoryModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LocalizationCategoryModel> { category });

        MockEntryQueryStore.Setup(q => q.QueryPagedAsync(
                It.IsAny<Expression<Func<LocalizationEntryModel, bool>>>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<Expression<Func<LocalizationEntryModel, object>>?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<LocalizationEntryModel>(
                new List<LocalizationEntryModel> { entry }, 1, 0, 5000));

        var request = new ExportRequest { Language = "en" };

        var (status, response) = await Service.ExportLocalizationAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("en", response.Language);
        Assert.Equal(1, response.EntryCount);

        // Verify cache was written
        MockCompiledCache.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<StateOptions?>(o => o != null && o.Ttl > 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExportLocalizationAsync_SpecificCategoryNotFound_ReturnsNotFound()
    {
        var request = new ExportRequest { Language = "en", CategoryId = Guid.NewGuid() };

        var (status, response) = await Service.ExportLocalizationAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ExportPlsAsync_EntriesWithPronunciation_ReturnsPls()
    {
        var category = CreateTestCategory(code: "items");
        SetupCategoryExists(category);

        var entry = CreateTestEntry(category.CategoryId, "dragon", "en", "Dragon");
        entry.Pronunciation = "ˈdɹæɡ.ən";

        MockEntryQueryStore.Setup(q => q.QueryAsync(
                It.IsAny<Expression<Func<LocalizationEntryModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LocalizationEntryModel> { entry });

        var request = new ExportPlsRequest { Language = "en", CategoryId = category.CategoryId };

        var (status, response) = await Service.ExportPlsAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.EntryCount);
        Assert.Contains("<lexicon", response.PlsXml);
        Assert.Contains("<grapheme>dragon</grapheme>", response.PlsXml);
        Assert.Contains("<phoneme>", response.PlsXml);
    }

    [Fact]
    public async Task ExportPlsAsync_NoPronunciationEntries_ReturnsEmptyLexicon()
    {
        var category = CreateTestCategory();
        SetupCategoryExists(category);

        MockEntryQueryStore.Setup(q => q.QueryAsync(
                It.IsAny<Expression<Func<LocalizationEntryModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LocalizationEntryModel>());

        var request = new ExportPlsRequest { Language = "en", CategoryId = category.CategoryId };

        var (status, response) = await Service.ExportPlsAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.EntryCount);
        Assert.Contains("<lexicon", response.PlsXml);
    }
}
