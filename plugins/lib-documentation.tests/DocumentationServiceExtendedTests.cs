using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Documentation;
using BeyondImmersion.BannouService.Documentation.Models;
using BeyondImmersion.BannouService.Documentation.Services;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;
using Xunit;

namespace BeyondImmersion.BannouService.Documentation.Tests;

/// <summary>
/// Extended unit tests for DocumentationService covering archive CRUD,
/// trashcan, recovery, bulk operations, import, sync, view, and suggestions.
/// </summary>
public class DocumentationServiceExtendedTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<DocumentationService.StoredDocument>> _mockDocumentStore;
    private readonly Mock<IStateStore<DocumentationService.TrashedDocument>> _mockTrashStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockGuidListStore;
    private readonly Mock<IStateStore<HashSet<Guid>>> _mockGuidSetStore;
    private readonly Mock<IStateStore<RepositoryBinding>> _mockBindingStore;
    private readonly Mock<IStateStore<HashSet<string>>> _mockRegistryStore;
    private readonly Mock<IStateStore<DocumentationArchive>> _mockArchiveStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<DocumentationService>> _mockLogger;
    private readonly DocumentationServiceConfiguration _configuration;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ISearchIndexService> _mockSearchIndexService;
    private readonly Mock<IGitSyncService> _mockGitSyncService;
    private readonly Mock<IContentTransformService> _mockContentTransformService;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly DocumentationService _service;

    private const string TEST_NAMESPACE = "test-namespace";
    private const string STATE_STORE = "documentation-statestore";

    public DocumentationServiceExtendedTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockDocumentStore = new Mock<IStateStore<DocumentationService.StoredDocument>>();
        _mockTrashStore = new Mock<IStateStore<DocumentationService.TrashedDocument>>();
        _mockGuidListStore = new Mock<IStateStore<List<Guid>>>();
        _mockGuidSetStore = new Mock<IStateStore<HashSet<Guid>>>();
        _mockBindingStore = new Mock<IStateStore<RepositoryBinding>>();
        _mockRegistryStore = new Mock<IStateStore<HashSet<string>>>();
        _mockArchiveStore = new Mock<IStateStore<DocumentationArchive>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<DocumentationService>>();
        _configuration = new DocumentationServiceConfiguration
        {
            SearchCacheTtlSeconds = 0 // Disable static cache to prevent test interference
        };
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockSearchIndexService = new Mock<ISearchIndexService>();
        _mockGitSyncService = new Mock<IGitSyncService>();
        _mockContentTransformService = new Mock<IContentTransformService>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<DocumentationService.StoredDocument>(STATE_STORE))
            .Returns(_mockDocumentStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<DocumentationService.TrashedDocument>(STATE_STORE))
            .Returns(_mockTrashStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE))
            .Returns(_mockGuidListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<HashSet<Guid>>(STATE_STORE))
            .Returns(_mockGuidSetStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<RepositoryBinding>(STATE_STORE))
            .Returns(_mockBindingStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<HashSet<string>>(STATE_STORE))
            .Returns(_mockRegistryStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<DocumentationArchive>(STATE_STORE))
            .Returns(_mockArchiveStore.Object);

        _service = new DocumentationService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object,
            _mockSearchIndexService.Object,
            _mockGitSyncService.Object,
            _mockContentTransformService.Object,
            _mockLockProvider.Object,
            _mockServiceProvider.Object,
            _mockHttpClientFactory.Object,
            _mockTelemetryProvider.Object);
    }

    #region Helpers

    /// <summary>
    /// Creates a StoredDocument for testing with required fields populated.
    /// </summary>
    private static DocumentationService.StoredDocument CreateTestStoredDocument(
        Guid? documentId = null,
        string? slug = null,
        string? title = null,
        string? content = null,
        string? namespaceId = null)
    {
        return new DocumentationService.StoredDocument
        {
            DocumentId = documentId ?? Guid.NewGuid(),
            Namespace = namespaceId ?? TEST_NAMESPACE,
            Slug = slug ?? "test-slug",
            Title = title ?? "Test Title",
            Category = DocumentCategory.GettingStarted,
            Content = content ?? "# Test Content\n\nSome markdown content.",
            Summary = "Test summary",
            VoiceSummary = "A short voice summary.",
            Tags = new List<string> { "tag1", "tag2" },
            RelatedDocuments = new List<Guid>(),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a TrashedDocument for testing with configurable expiry.
    /// </summary>
    private static DocumentationService.TrashedDocument CreateTestTrashedDocument(
        Guid? documentId = null,
        string? slug = null,
        bool expired = false)
    {
        var doc = CreateTestStoredDocument(documentId: documentId, slug: slug);
        return new DocumentationService.TrashedDocument
        {
            Document = doc,
            DeletedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = expired
                ? DateTimeOffset.UtcNow.AddDays(-1)
                : DateTimeOffset.UtcNow.AddDays(6)
        };
    }

    /// <summary>
    /// Sets up a mock lock that always succeeds.
    /// </summary>
    private void SetupSuccessfulLock()
    {
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider.Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    /// <summary>
    /// Sets up a mock lock that fails (lock not acquired).
    /// </summary>
    private void SetupFailedLock()
    {
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(false);
        _mockLockProvider.Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    #endregion

    #region ViewDocumentBySlugAsync Tests

    [Fact]
    public async Task ViewDocumentBySlugAsync_WithExistingDocument_ShouldReturnHtmlContent()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var storedDoc = CreateTestStoredDocument(documentId: documentId, slug: "my-doc", title: "My Document");

        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx") && k.Contains("my-doc")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId.ToString());

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(documentId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedDoc);

        // Act
        var (status, html) = await _service.ViewDocumentBySlugAsync("my-doc", TEST_NAMESPACE);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(html);
        Assert.Contains("My Document", html);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<article>", html);
    }

    [Fact]
    public async Task ViewDocumentBySlugAsync_WithNonExistentSlug_ShouldReturnNotFound()
    {
        // Arrange
        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, html) = await _service.ViewDocumentBySlugAsync("nonexistent-slug", TEST_NAMESPACE);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(html);
    }

    [Fact]
    public async Task ViewDocumentBySlugAsync_WithNullContent_ShouldReturnInternalServerError()
    {
        // Arrange - document exists but Content is null (data integrity issue)
        var documentId = Guid.NewGuid();
        var storedDoc = CreateTestStoredDocument(documentId: documentId, slug: "bad-doc");
        storedDoc.Content = null;

        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx") && k.Contains("bad-doc")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId.ToString());

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(documentId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedDoc);

        // Act
        var (status, html) = await _service.ViewDocumentBySlugAsync("bad-doc", TEST_NAMESPACE);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(html);

        // Verify error event was published
        _mockMessageBus.Verify(x => x.TryPublishErrorAsync(
            "documentation",
            "BrowseDocument",
            "DataIntegrityError",
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ViewDocumentBySlugAsync_WithSlugInIndexButDocMissing_ShouldReturnNotFound()
    {
        // Arrange - slug index points to document that doesn't exist
        var documentId = Guid.NewGuid();

        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId.ToString());

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(documentId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentationService.StoredDocument?)null);

        // Act
        var (status, html) = await _service.ViewDocumentBySlugAsync("orphan-slug", TEST_NAMESPACE);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(html);
    }

    [Fact]
    public async Task ViewDocumentBySlugAsync_WithDefaultNamespace_ShouldUseBannouNamespace()
    {
        // Arrange - use default namespace (null -> "bannou")
        var documentId = Guid.NewGuid();
        var storedDoc = CreateTestStoredDocument(documentId: documentId, content: "Hello world");

        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx") && k.Contains("bannou:")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId.ToString());

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedDoc);

        // Act
        var (status, html) = await _service.ViewDocumentBySlugAsync("test-slug", null);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(html);
    }

    #endregion

    #region SuggestRelatedTopicsAsync Tests

    [Fact]
    public async Task SuggestRelatedTopicsAsync_WithEmptySourceValue_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new SuggestRelatedRequest
        {
            Namespace = TEST_NAMESPACE,
            SuggestionSource = SuggestionSource.DocumentId,
            SourceValue = ""
        };

        // Act
        var (status, response) = await _service.SuggestRelatedTopicsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SuggestRelatedTopicsAsync_WithDocumentIdSource_ShouldReturnSuggestionsWithDocumentIdReason()
    {
        // Arrange
        var sourceDocId = Guid.NewGuid();
        var suggestedDocId = Guid.NewGuid();
        var suggestedDoc = CreateTestStoredDocument(documentId: suggestedDocId, title: "Related Doc");

        var request = new SuggestRelatedRequest
        {
            Namespace = TEST_NAMESPACE,
            SuggestionSource = SuggestionSource.DocumentId,
            SourceValue = sourceDocId.ToString(),
            MaxSuggestions = 5
        };

        _mockSearchIndexService.Setup(x => x.GetRelatedSuggestionsAsync(
            TEST_NAMESPACE, sourceDocId.ToString(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { suggestedDocId });

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(suggestedDocId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestedDoc);

        // Act
        var (status, response) = await _service.SuggestRelatedTopicsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Suggestions);
        Assert.Equal(suggestedDocId, response.Suggestions.First().DocumentId);
        Assert.Equal("Related Doc", response.Suggestions.First().Title);
        Assert.Contains("Related to document", response.Suggestions.First().RelevanceReason);
    }

    [Fact]
    public async Task SuggestRelatedTopicsAsync_WithSlugSource_ShouldReturnSlugRelevanceReason()
    {
        // Arrange
        var suggestedDocId = Guid.NewGuid();
        var suggestedDoc = CreateTestStoredDocument(documentId: suggestedDocId);

        var request = new SuggestRelatedRequest
        {
            Namespace = TEST_NAMESPACE,
            SuggestionSource = SuggestionSource.Slug,
            SourceValue = "getting-started",
            MaxSuggestions = 3
        };

        _mockSearchIndexService.Setup(x => x.GetRelatedSuggestionsAsync(
            TEST_NAMESPACE, "getting-started", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { suggestedDocId });

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestedDoc);

        // Act
        var (status, response) = await _service.SuggestRelatedTopicsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Suggestions);
        Assert.Contains("Similar to", response.Suggestions.First().RelevanceReason);
    }

    [Fact]
    public async Task SuggestRelatedTopicsAsync_WithTopicSource_ShouldReturnTopicRelevanceReason()
    {
        // Arrange
        var suggestedDocId = Guid.NewGuid();
        var suggestedDoc = CreateTestStoredDocument(documentId: suggestedDocId);

        var request = new SuggestRelatedRequest
        {
            Namespace = TEST_NAMESPACE,
            SuggestionSource = SuggestionSource.Topic,
            SourceValue = "authentication",
            MaxSuggestions = 5
        };

        _mockSearchIndexService.Setup(x => x.GetRelatedSuggestionsAsync(
            TEST_NAMESPACE, "authentication", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { suggestedDocId });

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestedDoc);

        // Act
        var (status, response) = await _service.SuggestRelatedTopicsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Contains("Covers topic", response.Suggestions.First().RelevanceReason);
    }

    [Fact]
    public async Task SuggestRelatedTopicsAsync_WithCategorySource_ShouldReturnCategoryRelevanceReason()
    {
        // Arrange
        var suggestedDocId = Guid.NewGuid();
        var suggestedDoc = CreateTestStoredDocument(documentId: suggestedDocId);

        var request = new SuggestRelatedRequest
        {
            Namespace = TEST_NAMESPACE,
            SuggestionSource = SuggestionSource.Category,
            SourceValue = "GettingStarted",
            MaxSuggestions = 5
        };

        _mockSearchIndexService.Setup(x => x.GetRelatedSuggestionsAsync(
            TEST_NAMESPACE, "GettingStarted", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { suggestedDocId });

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestedDoc);

        // Act
        var (status, response) = await _service.SuggestRelatedTopicsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Contains("In category", response.Suggestions.First().RelevanceReason);
    }

    [Fact]
    public async Task SuggestRelatedTopicsAsync_WithNoRelatedDocs_ShouldReturnEmptySuggestions()
    {
        // Arrange
        var request = new SuggestRelatedRequest
        {
            Namespace = TEST_NAMESPACE,
            SuggestionSource = SuggestionSource.Topic,
            SourceValue = "obscure-topic",
            MaxSuggestions = 5
        };

        _mockSearchIndexService.Setup(x => x.GetRelatedSuggestionsAsync(
            TEST_NAMESPACE, "obscure-topic", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Act
        var (status, response) = await _service.SuggestRelatedTopicsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Suggestions);
        Assert.Equal(TEST_NAMESPACE, response.Namespace);
    }

    #endregion

    #region RecoverDocumentAsync Tests

    [Fact]
    public async Task RecoverDocumentAsync_WithValidTrashedDocument_ShouldRestoreSuccessfully()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var trashedDoc = CreateTestTrashedDocument(documentId: documentId, slug: "recovered-doc");

        var request = new RecoverDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentId = documentId
        };

        _mockTrashStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("trash:") && k.Contains(documentId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(trashedDoc);

        // Slug not in use
        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Namespace docs index
        _mockGuidSetStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        // Capture state store save
        DocumentationService.StoredDocument? savedDoc = null;
        _mockDocumentStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<DocumentationService.StoredDocument>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DocumentationService.StoredDocument, StateOptions?, CancellationToken>(
                (_, doc, _, _) => savedDoc = doc)
            .ReturnsAsync("etag");

        // Trashcan index (List<Guid>)
        _mockGuidListStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("ns-trash")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { documentId });

        // Act
        var (status, response) = await _service.RecoverDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(documentId, response.DocumentId);
        Assert.True(response.RecoveredAt <= DateTimeOffset.UtcNow);
        Assert.NotNull(savedDoc);
        Assert.Equal(documentId, savedDoc.DocumentId);

        // Verify trashcan entry was deleted
        _mockTrashStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains("trash:") && k.Contains(documentId.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverDocumentAsync_WithExpiredTrashedDocument_ShouldReturnNotFound()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var expiredTrashedDoc = CreateTestTrashedDocument(documentId: documentId, expired: true);

        var request = new RecoverDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentId = documentId
        };

        _mockTrashStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("trash:")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredTrashedDoc);

        // Act
        var (status, response) = await _service.RecoverDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);

        // Verify expired entry was cleaned up
        _mockTrashStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains("trash:")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverDocumentAsync_WithSlugConflict_ShouldReturnConflict()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var trashedDoc = CreateTestTrashedDocument(documentId: documentId, slug: "taken-slug");

        var request = new RecoverDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentId = documentId
        };

        _mockTrashStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("trash:")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(trashedDoc);

        // Slug already in use by another document
        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx") && k.Contains("taken-slug")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        // Act
        var (status, response) = await _service.RecoverDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RecoverDocumentAsync_WithNotFoundInTrashcan_ShouldReturnNotFound()
    {
        // Arrange
        var request = new RecoverDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentId = Guid.NewGuid()
        };

        _mockTrashStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentationService.TrashedDocument?)null);

        // Act
        var (status, response) = await _service.RecoverDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region BulkUpdateDocumentsAsync Tests

    [Fact]
    public async Task BulkUpdateDocumentsAsync_WithExistingDocuments_ShouldUpdateAndReturnSuccess()
    {
        // Arrange
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var doc1 = CreateTestStoredDocument(documentId: docId1);
        var doc2 = CreateTestStoredDocument(documentId: docId2);

        var request = new BulkUpdateRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentIds = new List<Guid> { docId1, docId2 },
            Category = DocumentCategory.Tutorials,
            AddTags = new List<string> { "new-tag" }
        };

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(docId1.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc1);

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(docId2.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc2);

        // Act
        var (status, response) = await _service.BulkUpdateDocumentsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Succeeded.Count);
        Assert.Contains(docId1, response.Succeeded);
        Assert.Contains(docId2, response.Succeeded);
        Assert.Empty(response.Failed);
    }

    [Fact]
    public async Task BulkUpdateDocumentsAsync_WithMixedResults_ShouldReportFailures()
    {
        // Arrange
        var existingDocId = Guid.NewGuid();
        var missingDocId = Guid.NewGuid();
        var existingDoc = CreateTestStoredDocument(documentId: existingDocId);

        var request = new BulkUpdateRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentIds = new List<Guid> { existingDocId, missingDocId },
            AddTags = new List<string> { "bulk-tag" }
        };

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(existingDocId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(missingDocId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentationService.StoredDocument?)null);

        // Act
        var (status, response) = await _service.BulkUpdateDocumentsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Succeeded);
        Assert.Contains(existingDocId, response.Succeeded);
        Assert.Single(response.Failed);
        Assert.Equal(missingDocId, response.Failed.First().DocumentId);
        Assert.Equal("Document not found", response.Failed.First().Error);
    }

    [Fact]
    public async Task BulkUpdateDocumentsAsync_WithRemoveTags_ShouldRemoveSpecifiedTags()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var doc = CreateTestStoredDocument(documentId: docId);
        doc.Tags = new List<string> { "keep-me", "remove-me" };

        var request = new BulkUpdateRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentIds = new List<Guid> { docId },
            RemoveTags = new List<string> { "remove-me" }
        };

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(docId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        // Capture saved document
        DocumentationService.StoredDocument? savedDoc = null;
        _mockDocumentStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<DocumentationService.StoredDocument>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DocumentationService.StoredDocument, StateOptions?, CancellationToken>(
                (_, d, _, _) => savedDoc = d)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await _service.BulkUpdateDocumentsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Succeeded);
        Assert.NotNull(savedDoc);
        Assert.Contains("keep-me", savedDoc.Tags);
        Assert.DoesNotContain("remove-me", savedDoc.Tags);
    }

    #endregion

    #region BulkDeleteDocumentsAsync Tests

    [Fact]
    public async Task BulkDeleteDocumentsAsync_WithExistingDocuments_ShouldMoveToTrashAndReturnSuccess()
    {
        // Arrange
        var docId1 = Guid.NewGuid();
        var doc1 = CreateTestStoredDocument(documentId: docId1, slug: "doc-1");

        var request = new BulkDeleteRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentIds = new List<Guid> { docId1 }
        };

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(docId1.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc1);

        _mockGuidListStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("ns-trash")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Namespace docs index
        _mockGuidSetStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { docId1 });

        // Capture the trashed document
        DocumentationService.TrashedDocument? savedTrashedDoc = null;
        _mockTrashStore.Setup(s => s.SaveAsync(
            It.Is<string>(k => k.Contains("trash:")),
            It.IsAny<DocumentationService.TrashedDocument>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DocumentationService.TrashedDocument, StateOptions?, CancellationToken>(
                (_, td, _, _) => savedTrashedDoc = td)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await _service.BulkDeleteDocumentsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Succeeded);
        Assert.Contains(docId1, response.Succeeded);
        Assert.Empty(response.Failed);

        // Verify trashcan entry was created
        Assert.NotNull(savedTrashedDoc);
        Assert.Equal(docId1, savedTrashedDoc.Document.DocumentId);
        Assert.True(savedTrashedDoc.ExpiresAt > DateTimeOffset.UtcNow);

        // Verify original document was deleted
        _mockDocumentStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(docId1.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkDeleteDocumentsAsync_WithMissingDocument_ShouldReportFailure()
    {
        // Arrange
        var missingDocId = Guid.NewGuid();

        var request = new BulkDeleteRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentIds = new List<Guid> { missingDocId }
        };

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentationService.StoredDocument?)null);

        // Act
        var (status, response) = await _service.BulkDeleteDocumentsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Succeeded);
        Assert.Single(response.Failed);
        Assert.Equal("Document not found", response.Failed.First().Error);
    }

    #endregion

    #region ImportDocumentationAsync Tests

    [Fact]
    public async Task ImportDocumentationAsync_WithNewDocuments_ShouldCreateAndReturnCounts()
    {
        // Arrange
        var request = new ImportDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            Documents = new List<ImportDocument>
            {
                new ImportDocument
                {
                    Slug = "imported-doc",
                    Title = "Imported Document",
                    Category = DocumentCategory.Tutorials,
                    Content = "Imported content"
                }
            },
            OnConflict = ConflictResolution.Skip
        };

        // Slug doesn't exist yet
        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Namespace docs index
        _mockGuidSetStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        // Act
        var (status, response) = await _service.ImportDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Empty(response.Failed);
        Assert.Equal(TEST_NAMESPACE, response.Namespace);
    }

    [Fact]
    public async Task ImportDocumentationAsync_WithConflictSkip_ShouldSkipExisting()
    {
        // Arrange
        var existingDocId = Guid.NewGuid();

        var request = new ImportDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            Documents = new List<ImportDocument>
            {
                new ImportDocument
                {
                    Slug = "existing-slug",
                    Title = "Existing Document",
                    Category = DocumentCategory.Tutorials,
                    Content = "Content"
                }
            },
            OnConflict = ConflictResolution.Skip
        };

        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx") && k.Contains("existing-slug")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDocId.ToString());

        // Act
        var (status, response) = await _service.ImportDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(1, response.Skipped);
        Assert.Empty(response.Failed);
    }

    [Fact]
    public async Task ImportDocumentationAsync_WithConflictFail_ShouldFailOnExisting()
    {
        // Arrange
        var existingDocId = Guid.NewGuid();

        var request = new ImportDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            Documents = new List<ImportDocument>
            {
                new ImportDocument
                {
                    Slug = "existing-slug",
                    Title = "Existing Document",
                    Category = DocumentCategory.Tutorials,
                    Content = "Content"
                }
            },
            OnConflict = ConflictResolution.Fail
        };

        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDocId.ToString());

        // Act
        var (status, response) = await _service.ImportDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Single(response.Failed);
        Assert.Equal("existing-slug", response.Failed.First().Slug);
        Assert.Equal("Document already exists", response.Failed.First().Error);
    }

    [Fact]
    public async Task ImportDocumentationAsync_WithConflictUpdate_ShouldUpdateExisting()
    {
        // Arrange
        var existingDocId = Guid.NewGuid();
        var existingDoc = CreateTestStoredDocument(documentId: existingDocId, slug: "update-me");

        var request = new ImportDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            Documents = new List<ImportDocument>
            {
                new ImportDocument
                {
                    Slug = "update-me",
                    Title = "Updated Title",
                    Category = DocumentCategory.Architecture,
                    Content = "Updated content"
                }
            },
            OnConflict = ConflictResolution.Update
        };

        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDocId.ToString());

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(existingDocId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);

        // Act
        var (status, response) = await _service.ImportDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(1, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Empty(response.Failed);
    }

    [Fact]
    public async Task ImportDocumentationAsync_ExceedingMaxImportDocuments_ShouldReturnBadRequest()
    {
        // Arrange
        _configuration.MaxImportDocuments = 1;

        var request = new ImportDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            Documents = new List<ImportDocument>
            {
                new ImportDocument { Slug = "doc1", Title = "Doc 1", Category = DocumentCategory.Other, Content = "c1" },
                new ImportDocument { Slug = "doc2", Title = "Doc 2", Category = DocumentCategory.Other, Content = "c2" }
            }
        };

        // Act
        var (status, response) = await _service.ImportDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region ListTrashcanAsync Tests

    [Fact]
    public async Task ListTrashcanAsync_WithTrashedDocuments_ShouldReturnItems()
    {
        // Arrange
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var trashedDoc1 = CreateTestTrashedDocument(documentId: docId1);
        var trashedDoc2 = CreateTestTrashedDocument(documentId: docId2);

        var request = new ListTrashcanRequest
        {
            Namespace = TEST_NAMESPACE,
            Page = 1,
            PageSize = 20
        };

        _mockGuidListStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("ns-trash")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { docId1, docId2 });

        _mockTrashStore.Setup(s => s.GetBulkAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, DocumentationService.TrashedDocument?>
            {
                [$"trash:{TEST_NAMESPACE}:{docId1}"] = trashedDoc1,
                [$"trash:{TEST_NAMESPACE}:{docId2}"] = trashedDoc2
            });

        // Act
        var (status, response) = await _service.ListTrashcanAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TEST_NAMESPACE, response.Namespace);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task ListTrashcanAsync_WithEmptyTrashcan_ShouldReturnEmptyList()
    {
        // Arrange
        var request = new ListTrashcanRequest
        {
            Namespace = TEST_NAMESPACE,
            Page = 1,
            PageSize = 20
        };

        _mockGuidListStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("ns-trash")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act
        var (status, response) = await _service.ListTrashcanAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.TotalCount);
        Assert.Empty(response.Items);
    }

    #endregion

    #region PurgeTrashcanAsync Tests

    [Fact]
    public async Task PurgeTrashcanAsync_WithAllDocuments_ShouldPurgeAllAndReturnCount()
    {
        // Arrange
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();

        var request = new PurgeTrashcanRequest
        {
            Namespace = TEST_NAMESPACE
            // No DocumentIds = purge all
        };

        _mockGuidListStore.Setup(s => s.GetWithETagAsync(
            It.Is<string>(k => k.Contains("ns-trash")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { docId1, docId2 }, "etag-1"));

        // Act
        var (status, response) = await _service.PurgeTrashcanAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.PurgedCount);

        // Verify both trash entries deleted
        _mockTrashStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains("trash:")),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task PurgeTrashcanAsync_WithSpecificDocumentIds_ShouldPurgeOnlySpecified()
    {
        // Arrange
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();

        var request = new PurgeTrashcanRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentIds = new List<Guid> { docId1 }
        };

        _mockGuidListStore.Setup(s => s.GetWithETagAsync(
            It.Is<string>(k => k.Contains("ns-trash")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { docId1, docId2 }, "etag-1"));

        _mockGuidListStore.Setup(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<List<Guid>>(), It.IsAny<string>(),
            It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var (status, response) = await _service.PurgeTrashcanAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.PurgedCount);
    }

    [Fact]
    public async Task PurgeTrashcanAsync_WithEmptyTrashcan_ShouldReturnZeroPurged()
    {
        // Arrange
        var request = new PurgeTrashcanRequest
        {
            Namespace = TEST_NAMESPACE
        };

        _mockGuidListStore.Setup(s => s.GetWithETagAsync(
            It.Is<string>(k => k.Contains("ns-trash")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<Guid>?)null, (string?)null));

        // Act
        var (status, response) = await _service.PurgeTrashcanAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.PurgedCount);
    }

    #endregion

    #region GetNamespaceStatsAsync Tests

    [Fact]
    public async Task GetNamespaceStatsAsync_ShouldReturnAggregatedStats()
    {
        // Arrange
        var request = new GetNamespaceStatsRequest
        {
            Namespace = TEST_NAMESPACE
        };

        var searchStats = new NamespaceStats(
            TotalDocuments: 15,
            DocumentsByCategory: new Dictionary<DocumentCategory, int>
            {
                { DocumentCategory.GettingStarted, 5 },
                { DocumentCategory.ApiReference, 10 }
            },
            TotalTags: 8
        );

        _mockSearchIndexService.Setup(x => x.GetNamespaceStatsAsync(
            TEST_NAMESPACE, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchStats);

        _mockGuidListStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("ns-trash")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { Guid.NewGuid(), Guid.NewGuid() });

        // Last updated timestamp
        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("ns-last-updated")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTimeOffset.UtcNow.ToString("o"));

        // Act
        var (status, response) = await _service.GetNamespaceStatsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TEST_NAMESPACE, response.Namespace);
        Assert.Equal(15, response.DocumentCount);
        Assert.Equal(2, response.TrashcanCount);
        Assert.NotNull(response.CategoryCounts);
        Assert.Equal(2, response.CategoryCounts.Count);
        Assert.True(response.TotalContentSizeBytes > 0);
        Assert.NotNull(response.LastUpdated);
    }

    [Fact]
    public async Task GetNamespaceStatsAsync_WithNoLastUpdated_ShouldReturnNullLastUpdated()
    {
        // Arrange
        var request = new GetNamespaceStatsRequest
        {
            Namespace = TEST_NAMESPACE
        };

        var searchStats = new NamespaceStats(
            TotalDocuments: 0,
            DocumentsByCategory: new Dictionary<DocumentCategory, int>(),
            TotalTags: 0
        );

        _mockSearchIndexService.Setup(x => x.GetNamespaceStatsAsync(
            TEST_NAMESPACE, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchStats);

        _mockGuidListStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("ns-trash")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("ns-last-updated")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await _service.GetNamespaceStatsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.DocumentCount);
        Assert.Equal(0, response.TrashcanCount);
        Assert.Null(response.LastUpdated);
    }

    #endregion

    #region Archive CRUD Tests

    [Fact]
    public async Task CreateDocumentationArchiveAsync_WithEmptyNamespace_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateArchiveRequest
        {
            Namespace = "",
            OwnerType = DocumentationOwnerType.Session,
            OwnerId = "test-owner"
        };

        // Act
        var (status, response) = await _service.CreateDocumentationArchiveAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateDocumentationArchiveAsync_WithNoDocuments_ShouldReturnNotFound()
    {
        // Arrange
        var request = new CreateArchiveRequest
        {
            Namespace = TEST_NAMESPACE,
            OwnerType = DocumentationOwnerType.Session,
            OwnerId = "test-owner",
            Description = "Test archive"
        };

        // No documents in namespace
        _mockGuidSetStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("ns-docs")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<Guid>?)null);

        // Act
        var (status, response) = await _service.CreateDocumentationArchiveAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateDocumentationArchiveAsync_WithDocuments_ShouldCreateArchive()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var doc = CreateTestStoredDocument(documentId: docId, content: "Archive me");

        var request = new CreateArchiveRequest
        {
            Namespace = TEST_NAMESPACE,
            OwnerType = DocumentationOwnerType.Session,
            OwnerId = "test-owner",
            Description = "Snapshot before migration"
        };

        _mockGuidSetStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("ns-docs")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { docId });

        _mockDocumentStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(docId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        // No binding
        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepositoryBinding?)null);

        // Archive list store - GetWithETagAsync for optimistic concurrency
        _mockGuidListStore.Setup(s => s.GetWithETagAsync(
            It.Is<string>(k => k.Contains("archive:list")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<Guid>?)null, (string?)null));

        _mockGuidListStore.Setup(s => s.SaveAsync(
            It.Is<string>(k => k.Contains("archive:list")),
            It.IsAny<List<Guid>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // No asset client (graceful degradation) - returns null to simulate L3 service not available
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(BeyondImmersion.BannouService.Asset.IAssetClient)))
            .Returns((object?)null);

        // Act
        var (status, response) = await _service.CreateDocumentationArchiveAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.ArchiveId);
        Assert.Equal(TEST_NAMESPACE, response.Namespace);
        Assert.Equal(1, response.DocumentCount);
        Assert.True(response.SizeBytes > 0);
        Assert.Null(response.BundleAssetId); // No asset service
    }

    [Fact]
    public async Task ListDocumentationArchivesAsync_WithEmptyNamespace_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new ListArchivesRequest
        {
            Namespace = "",
            Limit = 20,
            Offset = 0
        };

        // Act
        var (status, response) = await _service.ListDocumentationArchivesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ListDocumentationArchivesAsync_WithExistingArchives_ShouldReturnList()
    {
        // Arrange
        var archiveId1 = Guid.NewGuid();
        var archiveId2 = Guid.NewGuid();

        var request = new ListArchivesRequest
        {
            Namespace = TEST_NAMESPACE,
            Limit = 20,
            Offset = 0
        };

        _mockGuidListStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("archive:list")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { archiveId1, archiveId2 });

        _mockArchiveStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(archiveId1.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentationArchive
            {
                ArchiveId = archiveId1,
                Namespace = TEST_NAMESPACE,
                DocumentCount = 5,
                SizeBytes = 1024,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
                OwnerType = OwnerTypeInternal.Session,
                OwnerId = "owner1"
            });

        _mockArchiveStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(archiveId2.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentationArchive
            {
                ArchiveId = archiveId2,
                Namespace = TEST_NAMESPACE,
                DocumentCount = 3,
                SizeBytes = 512,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                OwnerType = OwnerTypeInternal.Service,
                OwnerId = "owner2"
            });

        // Act
        var (status, response) = await _service.ListDocumentationArchivesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Total);
        Assert.Equal(2, response.Archives.Count);
    }

    [Fact]
    public async Task ListDocumentationArchivesAsync_WithNoArchives_ShouldReturnEmptyList()
    {
        // Arrange
        var request = new ListArchivesRequest
        {
            Namespace = TEST_NAMESPACE,
            Limit = 20,
            Offset = 0
        };

        _mockGuidListStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("archive:list")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act
        var (status, response) = await _service.ListDocumentationArchivesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Total);
        Assert.Empty(response.Archives);
    }

    [Fact]
    public async Task RestoreDocumentationArchiveAsync_WithNonExistentArchive_ShouldReturnNotFound()
    {
        // Arrange
        var request = new RestoreArchiveRequest
        {
            ArchiveId = Guid.NewGuid()
        };

        _mockArchiveStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentationArchive?)null);

        // Act
        var (status, response) = await _service.RestoreDocumentationArchiveAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RestoreDocumentationArchiveAsync_WithBoundNamespace_ShouldReturnForbidden()
    {
        // Arrange
        var archiveId = Guid.NewGuid();

        var request = new RestoreArchiveRequest
        {
            ArchiveId = archiveId
        };

        _mockArchiveStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(archiveId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentationArchive
            {
                ArchiveId = archiveId,
                Namespace = TEST_NAMESPACE,
                BundleAssetId = Guid.NewGuid()
            });

        // Namespace is bound to a repository
        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryBinding
            {
                BindingId = Guid.NewGuid(),
                Namespace = TEST_NAMESPACE,
                Status = BindingStatusInternal.Synced
            });

        // Act
        var (status, response) = await _service.RestoreDocumentationArchiveAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RestoreDocumentationArchiveAsync_WithNoBundleAssetId_ShouldReturnNotFound()
    {
        // Arrange - archive exists but has no bundle data
        var archiveId = Guid.NewGuid();

        var request = new RestoreArchiveRequest
        {
            ArchiveId = archiveId
        };

        _mockArchiveStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(archiveId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentationArchive
            {
                ArchiveId = archiveId,
                Namespace = TEST_NAMESPACE,
                BundleAssetId = null // No bundle uploaded
            });

        // No binding
        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepositoryBinding?)null);

        // Act
        var (status, response) = await _service.RestoreDocumentationArchiveAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteDocumentationArchiveAsync_WithExistingArchive_ShouldDeleteSuccessfully()
    {
        // Arrange
        var archiveId = Guid.NewGuid();

        var request = new DeleteArchiveRequest
        {
            ArchiveId = archiveId
        };

        _mockArchiveStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(archiveId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentationArchive
            {
                ArchiveId = archiveId,
                Namespace = TEST_NAMESPACE,
                DocumentCount = 5
            });

        // Archive list for deletion
        _mockGuidListStore.Setup(s => s.GetWithETagAsync(
            It.Is<string>(k => k.Contains("archive:list")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { archiveId }, "etag-1"));

        _mockGuidListStore.Setup(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<List<Guid>>(), It.IsAny<string>(),
            It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var (status, response) = await _service.DeleteDocumentationArchiveAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify archive record was deleted
        _mockArchiveStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(archiveId.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteDocumentationArchiveAsync_WithNonExistentArchive_ShouldReturnNotFound()
    {
        // Arrange
        var request = new DeleteArchiveRequest
        {
            ArchiveId = Guid.NewGuid()
        };

        _mockArchiveStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentationArchive?)null);

        // Act
        var (status, response) = await _service.DeleteDocumentationArchiveAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region SyncRepositoryAsync Tests

    [Fact]
    public async Task SyncRepositoryAsync_WithExistingBinding_ShouldExecuteSync()
    {
        // Arrange
        var bindingId = Guid.NewGuid();
        var binding = new RepositoryBinding
        {
            BindingId = bindingId,
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "https://github.com/test/repo.git",
            Branch = "main",
            Status = BindingStatusInternal.Synced,
            LastCommitHash = "abc123"
        };

        var request = new SyncRepositoryRequest
        {
            Namespace = TEST_NAMESPACE,
            Force = true
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(binding);

        SetupSuccessfulLock();

        // Git sync returns success with new commit
        _mockGitSyncService.Setup(g => g.SyncRepositoryAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitSyncResult(
                Success: true,
                CommitHash: "def456"));

        // No matching files (empty sync - just test the flow)
        _mockGitSyncService.Setup(g => g.GetMatchingFilesAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Registry store for SaveBindingAsync
        _mockRegistryStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { TEST_NAMESPACE });

        // Namespace docs for delete orphans
        _mockGuidSetStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<Guid>?)null);

        // Act
        var (status, response) = await _service.SyncRepositoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.SyncId);
    }

    [Fact]
    public async Task SyncRepositoryAsync_WithSyncLockFailure_ShouldReturnSyncInProgress()
    {
        // Arrange
        var binding = new RepositoryBinding
        {
            BindingId = Guid.NewGuid(),
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "https://github.com/test/repo.git",
            Branch = "main",
            Status = BindingStatusInternal.Synced
        };

        var request = new SyncRepositoryRequest
        {
            Namespace = TEST_NAMESPACE,
            Force = false
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(binding);

        SetupFailedLock();

        // Registry store for SaveBindingAsync
        _mockRegistryStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { TEST_NAMESPACE });

        // Act
        var (status, response) = await _service.SyncRepositoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Contains("already in progress", response.ErrorMessage);
    }

    #endregion
}
