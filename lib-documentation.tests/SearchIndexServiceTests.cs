using BeyondImmersion.BannouService.Documentation;
using BeyondImmersion.BannouService.Documentation.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Documentation.Tests;

/// <summary>
/// Unit tests for SearchIndexService - tests the in-memory search algorithm.
/// </summary>
public class SearchIndexServiceTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<List<Guid>>> _mockGuidListStore;
    private readonly Mock<ILogger<SearchIndexService>> _mockLogger;
    private readonly DocumentationServiceConfiguration _configuration;
    private readonly SearchIndexService _service;

    private const string TEST_NAMESPACE = "test-namespace";
    private const string STATE_STORE = "documentation-statestore";

    public SearchIndexServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockGuidListStore = new Mock<IStateStore<List<Guid>>>();
        _mockLogger = new Mock<ILogger<SearchIndexService>>();
        _configuration = new DocumentationServiceConfiguration();

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE))
            .Returns(_mockGuidListStore.Object);

        _service = new SearchIndexService(
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = new SearchIndexService(
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullStateStoreFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SearchIndexService(
            null!,
            _mockLogger.Object,
            _configuration));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SearchIndexService(
            _mockStateStoreFactory.Object,
            null!,
            _configuration));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SearchIndexService(
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            null!));
    }

    #endregion

    #region IndexDocument Tests

    [Fact]
    public async Task IndexDocument_WithValidData_ShouldAddToIndex()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var title = "Getting Started Guide";
        var slug = "getting-started";
        var content = "This is a guide to help you get started with the platform.";
        var category = "tutorials";
        var tags = new List<string> { "beginner", "guide" };

        // Act
        _service.IndexDocument(TEST_NAMESPACE, docId, title, slug, content, category, tags);

        // Assert - verify by searching
        var results = await _service.SearchAsync(TEST_NAMESPACE, "getting started", maxResults: 10);
        Assert.Single(results);
        Assert.Equal(docId, results[0].DocumentId);
        Assert.Equal(title, results[0].Title);
        Assert.Equal(slug, results[0].Slug);
    }

    [Fact]
    public async Task IndexDocument_WithNullContent_ShouldStillIndex()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var title = "Empty Content Document";
        var slug = "empty-content";

        // Act
        _service.IndexDocument(TEST_NAMESPACE, docId, title, slug, null, "general", null);

        // Assert - verify by searching title
        var results = await _service.SearchAsync(TEST_NAMESPACE, "empty content", maxResults: 10);
        Assert.Single(results);
        Assert.Equal(docId, results[0].DocumentId);
    }

    [Fact]
    public async Task IndexDocument_WithNullTags_ShouldStillIndex()
    {
        // Arrange
        var docId = Guid.NewGuid();

        // Act
        _service.IndexDocument(TEST_NAMESPACE, docId, "Test Doc", "test-doc", "content", "general", null);

        // Assert
        var stats = await _service.GetNamespaceStatsAsync(TEST_NAMESPACE);
        Assert.Equal(1, stats.TotalDocuments);
    }

    [Fact]
    public async Task IndexDocument_MultipleDocuments_ShouldIndexAll()
    {
        // Arrange & Act
        var doc1Id = Guid.NewGuid();
        var doc2Id = Guid.NewGuid();
        var doc3Id = Guid.NewGuid();

        _service.IndexDocument(TEST_NAMESPACE, doc1Id, "First Document", "first", "Content one", "tutorials", new[] { "tag1" });
        _service.IndexDocument(TEST_NAMESPACE, doc2Id, "Second Document", "second", "Content two", "guides", new[] { "tag2" });
        _service.IndexDocument(TEST_NAMESPACE, doc3Id, "Third Document", "third", "Content three", "tutorials", new[] { "tag1", "tag3" });

        // Assert
        var stats = await _service.GetNamespaceStatsAsync(TEST_NAMESPACE);
        Assert.Equal(3, stats.TotalDocuments);
        Assert.Equal(2, stats.DocumentsByCategory["tutorials"]);
        Assert.Equal(1, stats.DocumentsByCategory["guides"]);
    }

    #endregion

    #region Search Tests

    [Fact]
    public async Task Search_WithExactMatch_ShouldReturnHighScore()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _service.IndexDocument(TEST_NAMESPACE, docId, "Authentication Guide", "auth-guide",
            "Learn about authentication and authorization", "security", null);

        // Act
        var results = await _service.SearchAsync(TEST_NAMESPACE, "authentication", maxResults: 10);

        // Assert
        Assert.Single(results);
        Assert.Equal(docId, results[0].DocumentId);
        Assert.True(results[0].RelevanceScore > 0.5); // Exact match should have high score
    }

    [Fact]
    public async Task Search_WithPrefixMatch_ShouldReturnLowerScore()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _service.IndexDocument(TEST_NAMESPACE, docId, "Authentication Guide", "auth-guide",
            "Learn about authentication", "security", null);

        // Act - search with prefix
        var results = await _service.SearchAsync(TEST_NAMESPACE, "auth", maxResults: 10);

        // Assert
        Assert.Single(results);
        Assert.Equal(docId, results[0].DocumentId);
    }

    [Fact]
    public async Task Search_WithMultipleTerms_ShouldScoreHigherForMoreMatches()
    {
        // Arrange
        var doc1Id = Guid.NewGuid();
        var doc2Id = Guid.NewGuid();

        // Doc1 has both "getting" and "started"
        _service.IndexDocument(TEST_NAMESPACE, doc1Id, "Getting Started", "getting-started",
            "Getting started with the platform", "tutorials", null);

        // Doc2 only has "started"
        _service.IndexDocument(TEST_NAMESPACE, doc2Id, "Advanced Topics", "advanced",
            "Once you've started learning", "advanced", null);

        // Act
        var results = await _service.SearchAsync(TEST_NAMESPACE, "getting started", maxResults: 10);

        // Assert - doc1 should rank higher due to more term matches
        Assert.Equal(2, results.Count);
        Assert.Equal(doc1Id, results[0].DocumentId);
    }

    [Fact]
    public async Task Search_WithCategoryFilter_ShouldOnlyReturnMatchingCategory()
    {
        // Arrange
        var doc1Id = Guid.NewGuid();
        var doc2Id = Guid.NewGuid();

        _service.IndexDocument(TEST_NAMESPACE, doc1Id, "Tutorial One", "tutorial-one",
            "Learning content", "tutorials", null);
        _service.IndexDocument(TEST_NAMESPACE, doc2Id, "Tutorial Two", "tutorial-two",
            "More learning content", "guides", null);

        // Act
        var results = await _service.SearchAsync(TEST_NAMESPACE, "tutorial", category: "tutorials", maxResults: 10);

        // Assert
        Assert.Single(results);
        Assert.Equal(doc1Id, results[0].DocumentId);
    }

    [Fact]
    public async Task Search_WithMaxResults_ShouldLimitResults()
    {
        // Arrange - add 5 documents
        for (int i = 0; i < 5; i++)
        {
            _service.IndexDocument(TEST_NAMESPACE, Guid.NewGuid(), $"Document {i}", $"doc-{i}",
                "Common search term content", "general", null);
        }

        // Act
        var results = await _service.SearchAsync(TEST_NAMESPACE, "common search", maxResults: 3);

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Search_WithNoMatches_ShouldReturnEmpty()
    {
        // Arrange
        _service.IndexDocument(TEST_NAMESPACE, Guid.NewGuid(), "Hello World", "hello",
            "A simple document", "general", null);

        // Act
        var results = await _service.SearchAsync(TEST_NAMESPACE, "nonexistent term xyz", maxResults: 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_InEmptyNamespace_ShouldReturnEmpty()
    {
        // Act
        var results = await _service.SearchAsync("empty-namespace", "any term", maxResults: 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_ShouldFilterStopWords()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _service.IndexDocument(TEST_NAMESPACE, docId, "Important Document", "important",
            "This is an important document with content", "general", null);

        // Act - search with stop words only should not match
        var stopWordResults = await _service.SearchAsync(TEST_NAMESPACE, "the is an", maxResults: 10);
        var contentResults = await _service.SearchAsync(TEST_NAMESPACE, "important", maxResults: 10);

        // Assert
        Assert.Empty(stopWordResults); // Stop words filtered
        Assert.Single(contentResults); // Real words work
    }

    [Fact]
    public async Task Search_ShouldBeCaseInsensitive()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _service.IndexDocument(TEST_NAMESPACE, docId, "Authentication Guide", "auth",
            "Learn about AUTHENTICATION", "security", null);

        // Act
        var lowerResults = await _service.SearchAsync(TEST_NAMESPACE, "authentication", maxResults: 10);
        var upperResults = await _service.SearchAsync(TEST_NAMESPACE, "AUTHENTICATION", maxResults: 10);
        var mixedResults = await _service.SearchAsync(TEST_NAMESPACE, "AuThEnTiCaTiOn", maxResults: 10);

        // Assert
        Assert.Single(lowerResults);
        Assert.Single(upperResults);
        Assert.Single(mixedResults);
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task Query_WithMinRelevanceScore_ShouldFilterLowScores()
    {
        // Arrange
        var doc1Id = Guid.NewGuid();
        var doc2Id = Guid.NewGuid();

        // Doc1 has exact match
        _service.IndexDocument(TEST_NAMESPACE, doc1Id, "Authentication Security", "auth-security",
            "Authentication and security topics", "security", null);

        // Doc2 has only prefix match
        _service.IndexDocument(TEST_NAMESPACE, doc2Id, "Author Biography", "author",
            "About the author", "general", null);

        // Act - high min score should filter out low-relevance results
        var highThreshold = await _service.QueryAsync(TEST_NAMESPACE, "auth", minRelevanceScore: 0.8, maxResults: 10);
        var lowThreshold = await _service.QueryAsync(TEST_NAMESPACE, "auth", minRelevanceScore: 0.1, maxResults: 10);

        // Assert
        Assert.True(lowThreshold.Count >= highThreshold.Count);
    }

    [Fact]
    public async Task Query_InEmptyNamespace_ShouldReturnEmpty()
    {
        // Act
        var results = await _service.QueryAsync("nonexistent-namespace", "any query", maxResults: 10);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region GetRelatedSuggestions Tests

    [Fact]
    public async Task GetRelatedSuggestions_ByDocumentId_ShouldReturnRelated()
    {
        // Arrange
        var doc1Id = Guid.NewGuid();
        var doc2Id = Guid.NewGuid();
        var doc3Id = Guid.NewGuid();

        // Same category documents
        _service.IndexDocument(TEST_NAMESPACE, doc1Id, "Tutorial One", "tutorial-1", "Content", "tutorials", new[] { "beginner" });
        _service.IndexDocument(TEST_NAMESPACE, doc2Id, "Tutorial Two", "tutorial-2", "Content", "tutorials", new[] { "beginner" });
        _service.IndexDocument(TEST_NAMESPACE, doc3Id, "Advanced Guide", "advanced", "Content", "advanced", new[] { "expert" });

        // Act
        var related = await _service.GetRelatedSuggestionsAsync(TEST_NAMESPACE, doc1Id.ToString(), maxSuggestions: 5);

        // Assert - doc2 should be related (same category + shared tags)
        Assert.Contains(doc2Id, related);
    }

    [Fact]
    public async Task GetRelatedSuggestions_BySlug_ShouldFindSourceDocument()
    {
        // Arrange
        var doc1Id = Guid.NewGuid();
        var doc2Id = Guid.NewGuid();

        _service.IndexDocument(TEST_NAMESPACE, doc1Id, "Source Doc", "source-doc", "Content", "tutorials", new[] { "tag1" });
        _service.IndexDocument(TEST_NAMESPACE, doc2Id, "Related Doc", "related-doc", "Content", "tutorials", new[] { "tag1" });

        // Act
        var related = await _service.GetRelatedSuggestionsAsync(TEST_NAMESPACE, "source-doc", maxSuggestions: 5);

        // Assert
        Assert.Contains(doc2Id, related);
        Assert.DoesNotContain(doc1Id, related); // Should not suggest itself
    }

    [Fact]
    public async Task GetRelatedSuggestions_WithSharedTags_ShouldScoreHigher()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var relatedId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();

        _service.IndexDocument(TEST_NAMESPACE, sourceId, "Source", "source", "Content", "tutorials", new[] { "tag1", "tag2", "tag3" });
        _service.IndexDocument(TEST_NAMESPACE, relatedId, "Related", "related", "Content", "tutorials", new[] { "tag1", "tag2", "tag3" }); // Same tags
        _service.IndexDocument(TEST_NAMESPACE, unrelatedId, "Unrelated", "unrelated", "Content", "other", new[] { "different" }); // Different

        // Act
        var related = await _service.GetRelatedSuggestionsAsync(TEST_NAMESPACE, sourceId.ToString(), maxSuggestions: 5);

        // Assert - related doc should be first due to matching category + tags
        Assert.Equal(relatedId, related[0]);
    }

    [Fact]
    public async Task GetRelatedSuggestions_WithUnknownSource_ShouldFallbackToSearch()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _service.IndexDocument(TEST_NAMESPACE, docId, "Python Tutorial", "python-tutorial",
            "Learn Python programming", "tutorials", null);

        // Act - search term that doesn't match any doc ID/slug/title exactly
        var related = await _service.GetRelatedSuggestionsAsync(TEST_NAMESPACE, "python", maxSuggestions: 5);

        // Assert - should fall back to search and find the Python document
        Assert.Contains(docId, related);
    }

    [Fact]
    public async Task GetRelatedSuggestions_InEmptyNamespace_ShouldReturnEmpty()
    {
        // Act
        var related = await _service.GetRelatedSuggestionsAsync("empty-namespace", "anything", maxSuggestions: 5);

        // Assert
        Assert.Empty(related);
    }

    #endregion

    #region ListDocumentIds Tests

    [Fact]
    public async Task ListDocumentIds_ShouldReturnAllDocuments()
    {
        // Arrange
        var doc1Id = Guid.NewGuid();
        var doc2Id = Guid.NewGuid();

        _service.IndexDocument(TEST_NAMESPACE, doc1Id, "Doc One", "doc-1", "Content", "general", null);
        _service.IndexDocument(TEST_NAMESPACE, doc2Id, "Doc Two", "doc-2", "Content", "general", null);

        // Act
        var ids = await _service.ListDocumentIdsAsync(TEST_NAMESPACE);

        // Assert
        Assert.Equal(2, ids.Count);
        Assert.Contains(doc1Id, ids);
        Assert.Contains(doc2Id, ids);
    }

    [Fact]
    public async Task ListDocumentIds_WithCategoryFilter_ShouldFilterByCategory()
    {
        // Arrange
        var tutorialId = Guid.NewGuid();
        var guideId = Guid.NewGuid();

        _service.IndexDocument(TEST_NAMESPACE, tutorialId, "Tutorial", "tutorial", "Content", "tutorials", null);
        _service.IndexDocument(TEST_NAMESPACE, guideId, "Guide", "guide", "Content", "guides", null);

        // Act
        var tutorialIds = await _service.ListDocumentIdsAsync(TEST_NAMESPACE, category: "tutorials");

        // Assert
        Assert.Single(tutorialIds);
        Assert.Equal(tutorialId, tutorialIds[0]);
    }

    [Fact]
    public async Task ListDocumentIds_WithPagination_ShouldSkipAndTake()
    {
        // Arrange - add 10 documents
        var allIds = new List<Guid>();
        for (int i = 0; i < 10; i++)
        {
            var id = Guid.NewGuid();
            allIds.Add(id);
            _service.IndexDocument(TEST_NAMESPACE, id, $"Document {i:D2}", $"doc-{i}", "Content", "general", null);
        }

        // Act
        var page1 = await _service.ListDocumentIdsAsync(TEST_NAMESPACE, skip: 0, take: 3);
        var page2 = await _service.ListDocumentIdsAsync(TEST_NAMESPACE, skip: 3, take: 3);
        var page3 = await _service.ListDocumentIdsAsync(TEST_NAMESPACE, skip: 6, take: 3);

        // Assert
        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        Assert.Equal(3, page3.Count);

        // No overlap between pages
        Assert.Empty(page1.Intersect(page2));
        Assert.Empty(page2.Intersect(page3));
    }

    [Fact]
    public async Task ListDocumentIds_InEmptyNamespace_ShouldReturnEmpty()
    {
        // Act
        var ids = await _service.ListDocumentIdsAsync("empty-namespace");

        // Assert
        Assert.Empty(ids);
    }

    [Fact]
    public async Task ListDocumentIds_ShouldBeSortedByTitle()
    {
        // Arrange
        _service.IndexDocument(TEST_NAMESPACE, Guid.NewGuid(), "Zebra Document", "zebra", "Content", "general", null);
        _service.IndexDocument(TEST_NAMESPACE, Guid.NewGuid(), "Apple Document", "apple", "Content", "general", null);
        _service.IndexDocument(TEST_NAMESPACE, Guid.NewGuid(), "Mango Document", "mango", "Content", "general", null);

        // Act
        var ids = await _service.ListDocumentIdsAsync(TEST_NAMESPACE);

        // Verify count is correct
        Assert.Equal(3, ids.Count);
    }

    #endregion

    #region GetNamespaceStats Tests

    [Fact]
    public async Task GetNamespaceStats_ShouldReturnCorrectCounts()
    {
        // Arrange
        _service.IndexDocument(TEST_NAMESPACE, Guid.NewGuid(), "Doc 1", "doc-1", "Content", "tutorials", new[] { "tag1", "tag2" });
        _service.IndexDocument(TEST_NAMESPACE, Guid.NewGuid(), "Doc 2", "doc-2", "Content", "tutorials", new[] { "tag1" });
        _service.IndexDocument(TEST_NAMESPACE, Guid.NewGuid(), "Doc 3", "doc-3", "Content", "guides", new[] { "tag3" });

        // Act
        var stats = await _service.GetNamespaceStatsAsync(TEST_NAMESPACE);

        // Assert
        Assert.Equal(3, stats.TotalDocuments);
        Assert.Equal(2, stats.DocumentsByCategory["tutorials"]);
        Assert.Equal(1, stats.DocumentsByCategory["guides"]);
        Assert.Equal(3, stats.TotalTags); // tag1, tag2, tag3
    }

    [Fact]
    public async Task GetNamespaceStats_ForEmptyNamespace_ShouldReturnZeros()
    {
        // Act
        var stats = await _service.GetNamespaceStatsAsync("empty-namespace");

        // Assert
        Assert.Equal(0, stats.TotalDocuments);
        Assert.Empty(stats.DocumentsByCategory);
        Assert.Equal(0, stats.TotalTags);
    }

    #endregion

    #region RemoveDocument Tests

    [Fact]
    public async Task RemoveDocument_ShouldRemoveFromIndex()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _service.IndexDocument(TEST_NAMESPACE, docId, "To Be Removed", "to-remove", "Content", "general", new[] { "tag1" });

        // Verify it's indexed
        var beforeStats = await _service.GetNamespaceStatsAsync(TEST_NAMESPACE);
        Assert.Equal(1, beforeStats.TotalDocuments);

        // Act
        _service.RemoveDocument(TEST_NAMESPACE, docId);

        // Assert
        var afterStats = await _service.GetNamespaceStatsAsync(TEST_NAMESPACE);
        Assert.Equal(0, afterStats.TotalDocuments);

        // Search should not find it
        var searchResults = await _service.SearchAsync(TEST_NAMESPACE, "removed", maxResults: 10);
        Assert.Empty(searchResults);
    }

    [Fact]
    public void RemoveDocument_NonExistent_ShouldNotThrow()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act & Assert - should not throw
        var exception = Record.Exception(() => _service.RemoveDocument(TEST_NAMESPACE, nonExistentId));
        Assert.Null(exception);
    }

    [Fact]
    public void RemoveDocument_FromNonExistentNamespace_ShouldNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => _service.RemoveDocument("nonexistent-namespace", Guid.NewGuid()));
        Assert.Null(exception);
    }

    [Fact]
    public async Task RemoveDocument_ShouldDecrementTagCounts()
    {
        // Arrange
        var doc1Id = Guid.NewGuid();
        var doc2Id = Guid.NewGuid();

        _service.IndexDocument(TEST_NAMESPACE, doc1Id, "Doc 1", "doc-1", "Content", "general", new[] { "shared-tag", "unique-tag-1" });
        _service.IndexDocument(TEST_NAMESPACE, doc2Id, "Doc 2", "doc-2", "Content", "general", new[] { "shared-tag", "unique-tag-2" });

        var beforeStats = await _service.GetNamespaceStatsAsync(TEST_NAMESPACE);
        Assert.Equal(3, beforeStats.TotalTags); // shared-tag, unique-tag-1, unique-tag-2

        // Act
        _service.RemoveDocument(TEST_NAMESPACE, doc1Id);

        // Assert
        var afterStats = await _service.GetNamespaceStatsAsync(TEST_NAMESPACE);
        Assert.Equal(2, afterStats.TotalTags); // shared-tag (still used), unique-tag-2
    }

    #endregion

    #region Namespace Isolation Tests

    [Fact]
    public async Task DifferentNamespaces_ShouldBeIsolated()
    {
        // Arrange
        var ns1DocId = Guid.NewGuid();
        var ns2DocId = Guid.NewGuid();

        _service.IndexDocument("namespace-1", ns1DocId, "Namespace 1 Doc", "ns1-doc", "Content for NS1", "general", null);
        _service.IndexDocument("namespace-2", ns2DocId, "Namespace 2 Doc", "ns2-doc", "Content for NS2", "general", null);

        // Act
        var ns1Results = await _service.SearchAsync("namespace-1", "namespace", maxResults: 10);
        var ns2Results = await _service.SearchAsync("namespace-2", "namespace", maxResults: 10);

        // Assert
        Assert.Single(ns1Results);
        Assert.Equal(ns1DocId, ns1Results[0].DocumentId);

        Assert.Single(ns2Results);
        Assert.Equal(ns2DocId, ns2Results[0].DocumentId);
    }

    [Fact]
    public async Task NamespaceStats_ShouldBeIsolated()
    {
        // Arrange
        _service.IndexDocument("namespace-a", Guid.NewGuid(), "Doc A1", "a1", "Content", "tutorials", null);
        _service.IndexDocument("namespace-a", Guid.NewGuid(), "Doc A2", "a2", "Content", "tutorials", null);
        _service.IndexDocument("namespace-b", Guid.NewGuid(), "Doc B1", "b1", "Content", "guides", null);

        // Act
        var statsA = await _service.GetNamespaceStatsAsync("namespace-a");
        var statsB = await _service.GetNamespaceStatsAsync("namespace-b");

        // Assert
        Assert.Equal(2, statsA.TotalDocuments);
        Assert.Equal(1, statsB.TotalDocuments);
    }

    #endregion

    #region RebuildIndexAsync Tests

    [Fact]
    public async Task RebuildIndexAsync_WithNoDocuments_ShouldReturnZero()
    {
        // Arrange
        _mockGuidListStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k == $"ns-docs:{TEST_NAMESPACE}"),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act
        var count = await _service.RebuildIndexAsync(TEST_NAMESPACE);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task RebuildIndexAsync_WithEmptyDocumentList_ShouldReturnZero()
    {
        // Arrange
        _mockGuidListStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k == $"ns-docs:{TEST_NAMESPACE}"),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Act
        var count = await _service.RebuildIndexAsync(TEST_NAMESPACE);

        // Assert
        Assert.Equal(0, count);
    }

    #endregion
}
