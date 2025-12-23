using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Documentation;
using BeyondImmersion.BannouService.Documentation.Services;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Documentation.Tests;

/// <summary>
/// Unit tests for DocumentationService
/// </summary>
public class DocumentationServiceTests
{
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<DocumentationService>> _mockLogger;
    private readonly DocumentationServiceConfiguration _configuration;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ISearchIndexService> _mockSearchIndexService;
    private readonly DocumentationService _service;

    private const string TEST_NAMESPACE = "test-namespace";

    public DocumentationServiceTests()
    {
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<DocumentationService>>();
        _configuration = new DocumentationServiceConfiguration();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockSearchIndexService = new Mock<ISearchIndexService>();

        _service = new DocumentationService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _configuration,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object,
            _mockSearchIndexService.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = new DocumentationService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _configuration,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object,
            _mockSearchIndexService.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullDaprClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DocumentationService(
            null!,
            _mockLogger.Object,
            _configuration,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object,
            _mockSearchIndexService.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DocumentationService(
            _mockDaprClient.Object,
            null!,
            _configuration,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object,
            _mockSearchIndexService.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DocumentationService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            null!,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object,
            _mockSearchIndexService.Object));
    }

    [Fact]
    public void Constructor_WithNullErrorEventEmitter_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DocumentationService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _configuration,
            null!,
            _mockEventConsumer.Object,
            _mockSearchIndexService.Object));
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DocumentationService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _configuration,
            _mockErrorEventEmitter.Object,
            null!,
            _mockSearchIndexService.Object));
    }

    [Fact]
    public void Constructor_WithNullSearchIndexService_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DocumentationService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _configuration,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object,
            null!));
    }

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

        // Mock - slug already exists
        _mockDaprClient.Setup(x => x.GetStateAsync<Guid?>(
            It.IsAny<string>(), It.Is<string>(k => k.Contains("slug-idx")), null, null, default))
            .ReturnsAsync(existingDocId);

        // Act
        var (status, response) = await _service.CreateDocumentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
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

        _mockSearchIndexService.Setup(x => x.Search(
            TEST_NAMESPACE, "test query", null, It.IsAny<int>()))
            .Returns(new List<SearchResult>());

        // Act
        var (status, _) = await _service.SearchDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockSearchIndexService.Verify(x => x.Search(
            TEST_NAMESPACE, "test query", null, It.IsAny<int>()), Times.Once);
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

        _mockSearchIndexService.Setup(x => x.Query(
            TEST_NAMESPACE, "how do I start", null, It.IsAny<int>(), It.IsAny<double>()))
            .Returns(new List<SearchResult>());

        // Act
        var (status, _) = await _service.QueryDocumentationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockSearchIndexService.Verify(x => x.Query(
            TEST_NAMESPACE, "how do I start", null, It.IsAny<int>(), It.IsAny<double>()), Times.Once);
    }

    #endregion
}

/// <summary>
/// Configuration tests for DocumentationService
/// </summary>
public class DocumentationConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new DocumentationServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }

    // TODO: Add configuration-specific tests
}
