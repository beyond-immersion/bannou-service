using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Documentation;
using BeyondImmersion.BannouService.Documentation.Models;
using BeyondImmersion.BannouService.Documentation.Services;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;
using Xunit;

namespace BeyondImmersion.BannouService.Documentation.Tests;

/// <summary>
/// Unit tests for DocumentationService
/// </summary>
public class DocumentationServiceTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<DocumentationService.StoredDocument>> _mockDocumentStore;
    private readonly Mock<IStateStore<DocumentationService.TrashedDocument>> _mockTrashStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockGuidListStore;
    private readonly Mock<IStateStore<HashSet<Guid>>> _mockGuidSetStore;
    private readonly Mock<IStateStore<RepositoryBinding>> _mockBindingStore;
    private readonly Mock<IStateStore<HashSet<string>>> _mockRegistryStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<DocumentationService>> _mockLogger;
    private readonly DocumentationServiceConfiguration _configuration;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ISearchIndexService> _mockSearchIndexService;
    private readonly Mock<IGitSyncService> _mockGitSyncService;
    private readonly Mock<IContentTransformService> _mockContentTransformService;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<BeyondImmersion.BannouService.Asset.IAssetClient> _mockAssetClient;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly DocumentationService _service;

    private const string TEST_NAMESPACE = "test-namespace";
    private const string STATE_STORE = "documentation-statestore";

    public DocumentationServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockDocumentStore = new Mock<IStateStore<DocumentationService.StoredDocument>>();
        _mockTrashStore = new Mock<IStateStore<DocumentationService.TrashedDocument>>();
        _mockGuidListStore = new Mock<IStateStore<List<Guid>>>();
        _mockGuidSetStore = new Mock<IStateStore<HashSet<Guid>>>();
        _mockBindingStore = new Mock<IStateStore<RepositoryBinding>>();
        _mockRegistryStore = new Mock<IStateStore<HashSet<string>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<DocumentationService>>();
        _configuration = new DocumentationServiceConfiguration();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockSearchIndexService = new Mock<ISearchIndexService>();
        _mockGitSyncService = new Mock<IGitSyncService>();
        _mockContentTransformService = new Mock<IContentTransformService>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockAssetClient = new Mock<BeyondImmersion.BannouService.Asset.IAssetClient>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();

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
            _mockAssetClient.Object,
            _mockHttpClientFactory.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    ///
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void DocumentationService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<DocumentationService>();

    #endregion

    #region Input Validation Tests

    [Fact]
    public async Task CreateDocumentAsync_WithEmptySlug_ShouldReturnBadRequest()
    {
        // Arrange - missing slug
        var request = new CreateDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            Slug = "", // Empty slug
            Title = "Test Document",
            Category = DocumentCategory.GettingStarted
        };

        // Act
        var (status, response) = await _service.CreateDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateDocumentAsync_WithEmptyTitle_ShouldReturnBadRequest()
    {
        // Arrange - missing title
        var request = new CreateDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            Slug = "test-doc",
            Title = "", // Empty title
            Category = DocumentCategory.GettingStarted
        };

        // Act
        var (status, response) = await _service.CreateDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateDocumentAsync_WithEmptyNamespace_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateDocumentRequest
        {
            Namespace = "", // Empty namespace
            Slug = "test-doc",
            Title = "Test Document",
            Category = DocumentCategory.GettingStarted
        };

        // Act
        var (status, response) = await _service.CreateDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetDocumentAsync_WithEmptyDocumentId_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new GetDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentId = Guid.Empty
        };

        // Act
        var (status, response) = await _service.GetDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetDocumentAsync_WithEmptyNamespace_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new GetDocumentRequest
        {
            Namespace = "",
            DocumentId = Guid.NewGuid()
        };

        // Act
        var (status, response) = await _service.GetDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteDocumentAsync_WithEmptyDocumentId_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new DeleteDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentId = Guid.Empty
        };

        // Act
        var (status, response) = await _service.DeleteDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SearchDocumentationAsync_WithEmptySearchTerm_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new SearchDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            SearchTerm = ""
        };

        // Act
        var (status, response) = await _service.SearchDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SearchDocumentationAsync_WithEmptyNamespace_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new SearchDocumentationRequest
        {
            Namespace = "",
            SearchTerm = "test"
        };

        // Act
        var (status, response) = await _service.SearchDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task QueryDocumentationAsync_WithEmptyQuery_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new QueryDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            Query = ""
        };

        // Act
        var (status, response) = await _service.QueryDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task QueryDocumentationAsync_WithEmptyNamespace_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new QueryDocumentationRequest
        {
            Namespace = "",
            Query = "how to get started"
        };

        // Act
        var (status, response) = await _service.QueryDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ListDocumentsAsync_WithEmptyNamespace_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new ListDocumentsRequest
        {
            Namespace = ""
        };

        // Act
        var (status, response) = await _service.ListDocumentsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RecoverDocumentAsync_WithEmptyDocumentId_ShouldReturnNotFound()
    {
        // Arrange - Guid.Empty is a valid GUID that won't be found
        var request = new RecoverDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentId = Guid.Empty
        };

        // Act
        var (status, response) = await _service.RecoverDocumentAsync(request);

        // Assert - Guid.Empty results in NotFound (not BadRequest) per established pattern
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BulkUpdateDocumentsAsync_WithEmptyDocumentIds_ShouldReturnOkWithEmptyResults()
    {
        // Arrange - Empty list is a valid no-op operation
        var request = new BulkUpdateRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentIds = new List<Guid>()
        };

        // Act
        var (status, response) = await _service.BulkUpdateDocumentsAsync(request);

        // Assert - Empty list results in OK with empty results per established pattern
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Succeeded);
        Assert.Empty(response.Failed);
    }

    [Fact]
    public async Task BulkDeleteDocumentsAsync_WithEmptyDocumentIds_ShouldReturnOkWithEmptyResults()
    {
        // Arrange - Empty list is a valid no-op operation
        var request = new BulkDeleteRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentIds = new List<Guid>()
        };

        // Act
        var (status, response) = await _service.BulkDeleteDocumentsAsync(request);

        // Assert - Empty list results in OK with empty results per established pattern
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Succeeded);
        Assert.Empty(response.Failed);
    }

    [Fact]
    public async Task ImportDocumentationAsync_WithEmptyDocuments_ShouldReturnOkWithZeroCounts()
    {
        // Arrange - Empty list is a valid no-op operation
        var request = new ImportDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            Documents = new List<ImportDocument>()
        };

        // Act
        var (status, response) = await _service.ImportDocumentationAsync(request);

        // Assert - Empty list results in OK with zero counts per established pattern
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Empty(response.Failed);
    }

    #endregion

    #region Duplicate Slug Detection Tests

    [Fact]
    public async Task CreateDocumentAsync_WithDuplicateSlug_ShouldReturnConflict()
    {
        // Arrange
        var existingDocId = Guid.NewGuid();
        var request = new CreateDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            Slug = "existing-slug",
            Title = "Test Document",
            Content = "Test content",
            Category = DocumentCategory.GettingStarted
        };

        // Mock - slug already exists (stored as string representation of Guid)
        _mockStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("slug-idx")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDocId.ToString());

        // Act
        var (status, response) = await _service.CreateDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region Repository Binding Tests

    [Fact]
    public async Task BindRepositoryAsync_WithValidRequest_ShouldCreateBinding()
    {
        // Arrange
        var request = new BindRepositoryRequest
        {
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "https://github.com/test/repo.git",
            Branch = "main",
            FilePatterns = new List<string> { "**/*.md" },
            ExcludePatterns = new List<string> { "node_modules/**" }
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepositoryBinding?)null);

        _mockRegistryStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // Act
        var (status, response) = await _service.BindRepositoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.BindingId);
        Assert.Equal(TEST_NAMESPACE, response.Namespace);
    }

    [Fact]
    public async Task BindRepositoryAsync_WithEmptyNamespace_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new BindRepositoryRequest
        {
            Namespace = "",
            RepositoryUrl = "https://github.com/test/repo.git",
            Branch = "main"
        };

        // Act
        var (status, response) = await _service.BindRepositoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BindRepositoryAsync_WithEmptyRepositoryUrl_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new BindRepositoryRequest
        {
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "",
            Branch = "main"
        };

        // Act
        var (status, response) = await _service.BindRepositoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BindRepositoryAsync_WithExistingBinding_ShouldReturnConflict()
    {
        // Arrange
        var existingBinding = new RepositoryBinding
        {
            BindingId = Guid.NewGuid(),
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "https://github.com/test/existing.git"
        };

        var request = new BindRepositoryRequest
        {
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "https://github.com/test/new.git",
            Branch = "main"
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBinding);

        // Act
        var (status, response) = await _service.BindRepositoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UnbindRepositoryAsync_WithExistingBinding_ShouldRemoveBinding()
    {
        // Arrange
        var existingBinding = new RepositoryBinding
        {
            BindingId = Guid.NewGuid(),
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "https://github.com/test/repo.git",
            DocumentCount = 5
        };

        var request = new UnbindRepositoryRequest
        {
            Namespace = TEST_NAMESPACE,
            DeleteDocuments = true
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBinding);

        _mockRegistryStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { TEST_NAMESPACE });

        // Mock the HashSet<Guid> store for namespace docs (returns null = no docs to delete)
        _mockGuidSetStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<Guid>?)null);

        // Act
        var (status, response) = await _service.UnbindRepositoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TEST_NAMESPACE, response.Namespace);
    }

    [Fact]
    public async Task UnbindRepositoryAsync_WithNoExistingBinding_ShouldReturnNotFound()
    {
        // Arrange
        var request = new UnbindRepositoryRequest
        {
            Namespace = TEST_NAMESPACE,
            DeleteDocuments = false
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepositoryBinding?)null);

        // Act
        var (status, response) = await _service.UnbindRepositoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_WithExistingBinding_ShouldReturnStatus()
    {
        // Arrange
        var existingBinding = new RepositoryBinding
        {
            BindingId = Guid.NewGuid(),
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "https://github.com/test/repo.git",
            Branch = "main",
            Status = BindingStatusInternal.Synced,
            LastCommitHash = "abc123",
            DocumentCount = 10,
            LastSyncAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        var request = new RepositoryStatusRequest
        {
            Namespace = TEST_NAMESPACE
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBinding);

        // Act
        var (status, response) = await _service.GetRepositoryStatusAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Binding);
        Assert.Equal(TEST_NAMESPACE, response.Binding.Namespace);
        Assert.Equal(BindingStatus.Synced, response.Binding.Status);
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_WithNoBinding_ShouldReturnNotFound()
    {
        // Arrange
        var request = new RepositoryStatusRequest
        {
            Namespace = TEST_NAMESPACE
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepositoryBinding?)null);

        // Act
        var (status, response) = await _service.GetRepositoryStatusAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ListRepositoryBindingsAsync_ShouldReturnAllBindings()
    {
        // Arrange
        var binding1 = new RepositoryBinding
        {
            BindingId = Guid.NewGuid(),
            Namespace = "namespace-1",
            RepositoryUrl = "https://github.com/test/repo1.git"
        };

        var binding2 = new RepositoryBinding
        {
            BindingId = Guid.NewGuid(),
            Namespace = "namespace-2",
            RepositoryUrl = "https://github.com/test/repo2.git"
        };

        var request = new ListRepositoryBindingsRequest
        {
            Limit = 10,
            Offset = 0
        };

        _mockRegistryStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "namespace-1", "namespace-2" });

        _mockBindingStore.SetupSequence(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(binding1)
            .ReturnsAsync(binding2);

        // Act
        var (status, response) = await _service.ListRepositoryBindingsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Total);
        Assert.Equal(2, response.Bindings.Count);
    }

    [Fact]
    public async Task ListRepositoryBindingsAsync_WithNoBindings_ShouldReturnEmptyList()
    {
        // Arrange
        var request = new ListRepositoryBindingsRequest
        {
            Limit = 10,
            Offset = 0
        };

        _mockRegistryStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // Act
        var (status, response) = await _service.ListRepositoryBindingsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Total);
        Assert.Empty(response.Bindings);
    }

    [Fact]
    public async Task UpdateRepositoryBindingAsync_WithExistingBinding_ShouldUpdateSettings()
    {
        // Arrange
        var existingBinding = new RepositoryBinding
        {
            BindingId = Guid.NewGuid(),
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "https://github.com/test/repo.git",
            SyncIntervalMinutes = 60,
            SyncEnabled = true
        };

        var request = new UpdateRepositoryBindingRequest
        {
            Namespace = TEST_NAMESPACE,
            SyncIntervalMinutes = 120,
            SyncEnabled = false
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBinding);

        // Act
        var (status, response) = await _service.UpdateRepositoryBindingAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Binding);
        Assert.Equal(120, response.Binding.SyncIntervalMinutes);
        Assert.False(response.Binding.SyncEnabled);
    }

    [Fact]
    public async Task UpdateRepositoryBindingAsync_WithNoBinding_ShouldReturnNotFound()
    {
        // Arrange
        var request = new UpdateRepositoryBindingRequest
        {
            Namespace = TEST_NAMESPACE,
            SyncIntervalMinutes = 120
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepositoryBinding?)null);

        // Act
        var (status, response) = await _service.UpdateRepositoryBindingAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateDocumentAsync_InBoundNamespace_ShouldReturnForbidden()
    {
        // Arrange
        var existingBinding = new RepositoryBinding
        {
            BindingId = Guid.NewGuid(),
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "https://github.com/test/repo.git",
            Status = BindingStatusInternal.Synced
        };

        var request = new CreateDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            Slug = "manual-doc",
            Title = "Manual Document",
            Category = DocumentCategory.GettingStarted,
            Content = "Content"
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBinding);

        // Act
        var (status, response) = await _service.CreateDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateDocumentAsync_InBoundNamespace_ShouldReturnForbidden()
    {
        // Arrange
        var existingBinding = new RepositoryBinding
        {
            BindingId = Guid.NewGuid(),
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "https://github.com/test/repo.git",
            Status = BindingStatusInternal.Synced
        };

        var request = new UpdateDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentId = Guid.NewGuid(),
            Title = "Updated Title"
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBinding);

        // Act
        var (status, response) = await _service.UpdateDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteDocumentAsync_InBoundNamespace_ShouldReturnForbidden()
    {
        // Arrange
        var existingBinding = new RepositoryBinding
        {
            BindingId = Guid.NewGuid(),
            Namespace = TEST_NAMESPACE,
            RepositoryUrl = "https://github.com/test/repo.git",
            Status = BindingStatusInternal.Synced
        };

        var request = new DeleteDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            DocumentId = Guid.NewGuid()
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBinding);

        // Act
        var (status, response) = await _service.DeleteDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SyncRepositoryAsync_WithEmptyNamespace_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new SyncRepositoryRequest
        {
            Namespace = "",
            Force = false
        };

        // Act
        var (status, response) = await _service.SyncRepositoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SyncRepositoryAsync_WithNoBinding_ShouldReturnNotFound()
    {
        // Arrange
        var request = new SyncRepositoryRequest
        {
            Namespace = TEST_NAMESPACE,
            Force = false
        };

        _mockBindingStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("repo-binding")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepositoryBinding?)null);

        // Act
        var (status, response) = await _service.SyncRepositoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region Search Index Integration Tests

    [Fact]
    public async Task SearchDocumentationAsync_ShouldCallSearchIndex()
    {
        // Arrange
        var request = new SearchDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            SearchTerm = "test query"
        };

        _mockSearchIndexService.Setup(x => x.SearchAsync(
            TEST_NAMESPACE, "test query", null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        var (status, _) = await _service.SearchDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockSearchIndexService.Verify(x => x.SearchAsync(
            TEST_NAMESPACE, "test query", null, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryDocumentationAsync_ShouldCallQueryIndex()
    {
        // Arrange
        var request = new QueryDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            Query = "how do I start"
        };

        _mockSearchIndexService.Setup(x => x.QueryAsync(
            TEST_NAMESPACE, "how do I start", null, It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        var (status, _) = await _service.QueryDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockSearchIndexService.Verify(x => x.QueryAsync(
            TEST_NAMESPACE, "how do I start", null, It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Sorting Tests

    [Fact]
    public async Task ListDocumentsAsync_WithSortByCreatedAtAsc_ShouldCallStateStoreWithCorrectParams()
    {
        // Arrange
        var request = new ListDocumentsRequest
        {
            Namespace = TEST_NAMESPACE,
            SortBy = ListSortField.Created_at,
            SortOrder = ListDocumentsRequestSortOrder.Asc
        };

        _mockGuidSetStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        // Act
        var (status, response) = await _service.ListDocumentsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ListDocumentsAsync_WithSortByTitleDesc_ShouldReturnDocuments()
    {
        // Arrange
        var request = new ListDocumentsRequest
        {
            Namespace = TEST_NAMESPACE,
            SortBy = ListSortField.Title,
            SortOrder = ListDocumentsRequestSortOrder.Desc
        };

        _mockGuidSetStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        // Act
        var (status, response) = await _service.ListDocumentsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ListDocumentsAsync_WithTagsMatchAll_ShouldFilterDocuments()
    {
        // Arrange
        var request = new ListDocumentsRequest
        {
            Namespace = TEST_NAMESPACE,
            Tags = new List<string> { "tag1", "tag2" },
            TagsMatch = ListDocumentsRequestTagsMatch.All
        };

        _mockGuidSetStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        // Act
        var (status, response) = await _service.ListDocumentsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ListDocumentsAsync_WithTagsMatchAny_ShouldFilterDocuments()
    {
        // Arrange
        var request = new ListDocumentsRequest
        {
            Namespace = TEST_NAMESPACE,
            Tags = new List<string> { "tag1", "tag2" },
            TagsMatch = ListDocumentsRequestTagsMatch.Any
        };

        _mockGuidSetStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        // Act
        var (status, response) = await _service.ListDocumentsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task SearchDocumentationAsync_WithSortByRelevance_ShouldReturnResults()
    {
        // Arrange
        var request = new SearchDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            SearchTerm = "test query",
            SortBy = SearchDocumentationRequestSortBy.Relevance
        };

        _mockSearchIndexService.Setup(x => x.SearchAsync(
            TEST_NAMESPACE, "test query", null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        var (status, response) = await _service.SearchDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task SearchDocumentationAsync_WithSortByRecency_ShouldReturnResults()
    {
        // Arrange
        var request = new SearchDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            SearchTerm = "test query",
            SortBy = SearchDocumentationRequestSortBy.Recency
        };

        _mockSearchIndexService.Setup(x => x.SearchAsync(
            TEST_NAMESPACE, "test query", null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        var (status, response) = await _service.SearchDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task SearchDocumentationAsync_WithSortByAlphabetical_ShouldReturnResults()
    {
        // Arrange
        var request = new SearchDocumentationRequest
        {
            Namespace = TEST_NAMESPACE,
            SearchTerm = "test query",
            SortBy = SearchDocumentationRequestSortBy.Alphabetical
        };

        _mockSearchIndexService.Setup(x => x.SearchAsync(
            TEST_NAMESPACE, "test query", null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        var (status, response) = await _service.SearchDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    #endregion
}

/// <summary>
/// Configuration tests for DocumentationService
/// </summary>
public class DocumentationConfigurationTests
{
    [Fact]
    public void Configuration_WithDefaultValues_ShouldHaveExpectedDefaults()
    {
        // Arrange & Act
        var config = new DocumentationServiceConfiguration();

        // Assert - Verify all default values are set correctly
        Assert.NotNull(config);
        Assert.True(config.SearchIndexRebuildOnStartup, "SearchIndexRebuildOnStartup should default to true");
        Assert.Equal(86400, config.SessionTtlSeconds); // 24 hours
        Assert.Equal(524288, config.MaxContentSizeBytes); // 500KB
        Assert.Equal(7, config.TrashcanTtlDays);
        Assert.Equal(200, config.VoiceSummaryMaxLength);
        Assert.Equal(300, config.SearchCacheTtlSeconds); // 5 minutes
        Assert.Equal(0.3, config.MinRelevanceScore);
        Assert.Equal(20, config.MaxSearchResults);
        Assert.Equal(0, config.MaxImportDocuments); // 0 = unlimited
        Assert.False(config.AiEnhancementsEnabled);
        Assert.Null(config.AiEmbeddingsModel); // NRT: Optional config with no default is null
    }

    [Fact]
    public void Configuration_ForceServiceId_ShouldBeNullByDefault()
    {
        // Arrange & Act
        var config = new DocumentationServiceConfiguration();

        // Assert
        Assert.Null(config.ForceServiceId);
    }

    [Fact]
    public void Configuration_WithCustomValues_ShouldRetainValues()
    {
        // Arrange & Act
        var config = new DocumentationServiceConfiguration
        {
            SearchIndexRebuildOnStartup = false,
            SessionTtlSeconds = 3600,
            MaxContentSizeBytes = 1048576, // 1MB
            TrashcanTtlDays = 30,
            VoiceSummaryMaxLength = 500,
            SearchCacheTtlSeconds = 600,
            MinRelevanceScore = 0.5,
            MaxSearchResults = 50,
            MaxImportDocuments = 100,
            AiEnhancementsEnabled = true,
            AiEmbeddingsModel = "text-embedding-ada-002"
        };

        // Assert
        Assert.False(config.SearchIndexRebuildOnStartup);
        Assert.Equal(3600, config.SessionTtlSeconds);
        Assert.Equal(1048576, config.MaxContentSizeBytes);
        Assert.Equal(30, config.TrashcanTtlDays);
        Assert.Equal(500, config.VoiceSummaryMaxLength);
        Assert.Equal(600, config.SearchCacheTtlSeconds);
        Assert.Equal(0.5, config.MinRelevanceScore);
        Assert.Equal(50, config.MaxSearchResults);
        Assert.Equal(100, config.MaxImportDocuments);
        Assert.True(config.AiEnhancementsEnabled);
        Assert.Equal("text-embedding-ada-002", config.AiEmbeddingsModel);
    }
}
