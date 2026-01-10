using BeyondImmersion.BannouService.History;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Moq;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Tests.History;

/// <summary>
/// Unit tests for DualIndexHelper.
/// </summary>
[Collection("unit tests")]
public class DualIndexHelperTests : IClassFixture<CollectionFixture>
{
    private const string TestStateStoreName = "test-store";
    private const string RecordPrefix = "record-";
    private const string PrimaryIndexPrefix = "primary-index-";
    private const string SecondaryIndexPrefix = "secondary-index-";

    private CollectionFixture TestCollectionContext { get; }

    public DualIndexHelperTests(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<DualIndexHelperTests>();
    }

    /// <summary>
    /// Simple test record for dual-index testing.
    /// </summary>
    public class TestRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
    }

    private (Mock<IStateStoreFactory>, Mock<IStateStore<TestRecord>>, Mock<IStateStore<HistoryIndexData>>) CreateMocks()
    {
        var mockFactory = new Mock<IStateStoreFactory>();
        var mockRecordStore = new Mock<IStateStore<TestRecord>>();
        var mockIndexStore = new Mock<IStateStore<HistoryIndexData>>();

        mockFactory.Setup(f => f.GetStore<TestRecord>(TestStateStoreName)).Returns(mockRecordStore.Object);
        mockFactory.Setup(f => f.GetStore<HistoryIndexData>(TestStateStoreName)).Returns(mockIndexStore.Object);

        return (mockFactory, mockRecordStore, mockIndexStore);
    }

    private DualIndexHelper<TestRecord> CreateHelper(Mock<IStateStoreFactory> mockFactory)
    {
        return new DualIndexHelper<TestRecord>(
            mockFactory.Object,
            TestStateStoreName,
            RecordPrefix,
            PrimaryIndexPrefix,
            SecondaryIndexPrefix);
    }

    [Fact]
    public void Constructor_WithNullFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DualIndexHelper<TestRecord>(
            null!,
            TestStateStoreName,
            RecordPrefix,
            PrimaryIndexPrefix,
            SecondaryIndexPrefix));
    }

    [Fact]
    public void Constructor_WithNullStoreName_ThrowsArgumentNullException()
    {
        var (mockFactory, _, _) = CreateMocks();

        Assert.Throws<ArgumentNullException>(() => new DualIndexHelper<TestRecord>(
            mockFactory.Object,
            null!,
            RecordPrefix,
            PrimaryIndexPrefix,
            SecondaryIndexPrefix));
    }

    [Fact]
    public void Constructor_WithConfiguration_SetsPropertiesCorrectly()
    {
        var (mockFactory, _, _) = CreateMocks();
        var config = new DualIndexConfiguration(
            mockFactory.Object,
            TestStateStoreName,
            RecordPrefix,
            PrimaryIndexPrefix,
            SecondaryIndexPrefix);

        var helper = new DualIndexHelper<TestRecord>(config);
        Assert.NotNull(helper);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DualIndexHelper<TestRecord>(null!));
    }

    [Fact]
    public async Task AddRecordAsync_SavesRecordAndUpdatesIndices()
    {
        // Arrange
        var (mockFactory, mockRecordStore, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var record = new TestRecord { Id = "rec-1", Name = "Test", EventId = "event-1" };
        var recordId = "rec-1";
        var primaryKey = "entity-1";
        var secondaryKey = "event-1";

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}{primaryKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);
        mockIndexStore.Setup(s => s.GetAsync($"{SecondaryIndexPrefix}{secondaryKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Act
        var result = await helper.AddRecordAsync(record, recordId, primaryKey, secondaryKey);

        // Assert
        Assert.Equal(recordId, result);

        mockRecordStore.Verify(s => s.SaveAsync(
            $"{RecordPrefix}{recordId}",
            record,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        mockIndexStore.Verify(s => s.SaveAsync(
            $"{PrimaryIndexPrefix}{primaryKey}",
            It.Is<HistoryIndexData>(i => i.RecordIds.Contains(recordId)),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        mockIndexStore.Verify(s => s.SaveAsync(
            $"{SecondaryIndexPrefix}{secondaryKey}",
            It.Is<HistoryIndexData>(i => i.RecordIds.Contains(recordId)),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddRecordAsync_WithExistingIndex_AppendsToList()
    {
        // Arrange
        var (mockFactory, mockRecordStore, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var record = new TestRecord { Id = "rec-2", Name = "Test", EventId = "event-1" };
        var recordId = "rec-2";
        var primaryKey = "entity-1";
        var secondaryKey = "event-1";

        var existingPrimaryIndex = new HistoryIndexData
        {
            EntityId = primaryKey,
            RecordIds = new List<string> { "rec-1" }
        };

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}{primaryKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPrimaryIndex);
        mockIndexStore.Setup(s => s.GetAsync($"{SecondaryIndexPrefix}{secondaryKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Act
        await helper.AddRecordAsync(record, recordId, primaryKey, secondaryKey);

        // Assert - Verify index now has both records
        mockIndexStore.Verify(s => s.SaveAsync(
            $"{PrimaryIndexPrefix}{primaryKey}",
            It.Is<HistoryIndexData>(i =>
                i.RecordIds.Contains("rec-1") && i.RecordIds.Contains("rec-2")),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddRecordAsync_WithNullRecord_ThrowsArgumentNullException()
    {
        var (mockFactory, _, _) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.AddRecordAsync(null!, "rec-1", "pk", "sk"));
    }

    [Fact]
    public async Task AddRecordAsync_WithEmptyRecordId_ThrowsArgumentNullException()
    {
        var (mockFactory, _, _) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.AddRecordAsync(new TestRecord(), "", "pk", "sk"));
    }

    [Fact]
    public async Task GetRecordAsync_WithValidId_ReturnsRecord()
    {
        // Arrange
        var (mockFactory, mockRecordStore, _) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var record = new TestRecord { Id = "rec-1", Name = "Test" };
        mockRecordStore.Setup(s => s.GetAsync($"{RecordPrefix}rec-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        // Act
        var result = await helper.GetRecordAsync("rec-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("rec-1", result.Id);
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public async Task GetRecordAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        var (mockFactory, mockRecordStore, _) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        mockRecordStore.Setup(s => s.GetAsync($"{RecordPrefix}non-existent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestRecord?)null);

        // Act
        var result = await helper.GetRecordAsync("non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecordAsync_WithEmptyId_ReturnsNull()
    {
        var (mockFactory, _, _) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var result = await helper.GetRecordAsync("");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecordIdsByPrimaryKeyAsync_ReturnsRecordIds()
    {
        // Arrange
        var (mockFactory, _, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var index = new HistoryIndexData
        {
            EntityId = "entity-1",
            RecordIds = new List<string> { "rec-1", "rec-2", "rec-3" }
        };

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);

        // Act
        var result = await helper.GetRecordIdsByPrimaryKeyAsync("entity-1");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("rec-1", result);
        Assert.Contains("rec-2", result);
        Assert.Contains("rec-3", result);
    }

    [Fact]
    public async Task GetRecordIdsByPrimaryKeyAsync_WithNoIndex_ReturnsEmptyList()
    {
        // Arrange
        var (mockFactory, _, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Act
        var result = await helper.GetRecordIdsByPrimaryKeyAsync("entity-1");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecordIdsByPrimaryKeyAsync_WithEmptyKey_ReturnsEmptyList()
    {
        var (mockFactory, _, _) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var result = await helper.GetRecordIdsByPrimaryKeyAsync("");
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecordIdsBySecondaryKeyAsync_ReturnsRecordIds()
    {
        // Arrange
        var (mockFactory, _, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var index = new HistoryIndexData
        {
            EntityId = "event-1",
            RecordIds = new List<string> { "rec-1", "rec-2" }
        };

        mockIndexStore.Setup(s => s.GetAsync($"{SecondaryIndexPrefix}event-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);

        // Act
        var result = await helper.GetRecordIdsBySecondaryKeyAsync("event-1");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("rec-1", result);
        Assert.Contains("rec-2", result);
    }

    [Fact]
    public async Task GetRecordsByPrimaryKeyAsync_ReturnsRecords()
    {
        // Arrange
        var (mockFactory, mockRecordStore, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var index = new HistoryIndexData
        {
            EntityId = "entity-1",
            RecordIds = new List<string> { "rec-1", "rec-2" }
        };

        var record1 = new TestRecord { Id = "rec-1", Name = "Record 1" };
        var record2 = new TestRecord { Id = "rec-2", Name = "Record 2" };

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);

        mockRecordStore.Setup(s => s.GetBulkAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, TestRecord>
            {
                { $"{RecordPrefix}rec-1", record1 },
                { $"{RecordPrefix}rec-2", record2 }
            });

        // Act
        var result = await helper.GetRecordsByPrimaryKeyAsync("entity-1");

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task RemoveRecordAsync_DeletesRecordAndUpdatesIndices()
    {
        // Arrange
        var (mockFactory, mockRecordStore, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var record = new TestRecord { Id = "rec-1", Name = "Test" };
        var recordKey = $"{RecordPrefix}rec-1";
        var primaryKey = "entity-1";
        var secondaryKey = "event-1";

        mockRecordStore.Setup(s => s.GetAsync(recordKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        mockRecordStore.Setup(s => s.DeleteAsync(recordKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var primaryIndex = new HistoryIndexData
        {
            EntityId = primaryKey,
            RecordIds = new List<string> { "rec-1", "rec-2" }
        };
        var secondaryIndex = new HistoryIndexData
        {
            EntityId = secondaryKey,
            RecordIds = new List<string> { "rec-1" }
        };

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}{primaryKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(primaryIndex);
        mockIndexStore.Setup(s => s.GetAsync($"{SecondaryIndexPrefix}{secondaryKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondaryIndex);

        // Act
        var result = await helper.RemoveRecordAsync("rec-1", primaryKey, secondaryKey);

        // Assert
        Assert.True(result);
        mockRecordStore.Verify(s => s.DeleteAsync(recordKey, It.IsAny<CancellationToken>()), Times.Once);

        // Verify indices were updated (rec-1 should be removed)
        mockIndexStore.Verify(s => s.SaveAsync(
            $"{PrimaryIndexPrefix}{primaryKey}",
            It.Is<HistoryIndexData>(i => !i.RecordIds.Contains("rec-1") && i.RecordIds.Contains("rec-2")),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        mockIndexStore.Verify(s => s.SaveAsync(
            $"{SecondaryIndexPrefix}{secondaryKey}",
            It.Is<HistoryIndexData>(i => !i.RecordIds.Contains("rec-1")),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveRecordAsync_WithNonExistentRecord_ReturnsFalse()
    {
        // Arrange
        var (mockFactory, mockRecordStore, _) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        mockRecordStore.Setup(s => s.GetAsync($"{RecordPrefix}rec-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestRecord?)null);

        // Act
        var result = await helper.RemoveRecordAsync("rec-1", "entity-1", "event-1");

        // Assert
        Assert.False(result);
        mockRecordStore.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveRecordAsync_WithEmptyId_ReturnsFalse()
    {
        var (mockFactory, _, _) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var result = await helper.RemoveRecordAsync("", "entity-1", "event-1");
        Assert.False(result);
    }

    [Fact]
    public async Task RemoveAllByPrimaryKeyAsync_DeletesAllRecordsAndUpdatesIndices()
    {
        // Arrange
        var (mockFactory, mockRecordStore, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var primaryKey = "entity-1";
        var primaryIndex = new HistoryIndexData
        {
            EntityId = primaryKey,
            RecordIds = new List<string> { "rec-1", "rec-2" }
        };

        var record1 = new TestRecord { Id = "rec-1", EventId = "event-1" };
        var record2 = new TestRecord { Id = "rec-2", EventId = "event-2" };

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}{primaryKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(primaryIndex);
        mockRecordStore.Setup(s => s.GetAsync($"{RecordPrefix}rec-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(record1);
        mockRecordStore.Setup(s => s.GetAsync($"{RecordPrefix}rec-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(record2);

        var event1Index = new HistoryIndexData { EntityId = "event-1", RecordIds = new List<string> { "rec-1" } };
        var event2Index = new HistoryIndexData { EntityId = "event-2", RecordIds = new List<string> { "rec-2" } };

        mockIndexStore.Setup(s => s.GetAsync($"{SecondaryIndexPrefix}event-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(event1Index);
        mockIndexStore.Setup(s => s.GetAsync($"{SecondaryIndexPrefix}event-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(event2Index);

        // Act
        var result = await helper.RemoveAllByPrimaryKeyAsync(primaryKey, r => r.EventId);

        // Assert
        Assert.Equal(2, result);
        mockRecordStore.Verify(s => s.DeleteAsync($"{RecordPrefix}rec-1", It.IsAny<CancellationToken>()), Times.Once);
        mockRecordStore.Verify(s => s.DeleteAsync($"{RecordPrefix}rec-2", It.IsAny<CancellationToken>()), Times.Once);
        mockIndexStore.Verify(s => s.DeleteAsync($"{PrimaryIndexPrefix}{primaryKey}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAllByPrimaryKeyAsync_WithEmptyKey_ReturnsZero()
    {
        var (mockFactory, _, _) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var result = await helper.RemoveAllByPrimaryKeyAsync("", r => r.EventId);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task RemoveAllByPrimaryKeyAsync_WithNullFunc_ThrowsArgumentNullException()
    {
        var (mockFactory, _, _) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.RemoveAllByPrimaryKeyAsync("entity-1", null!));
    }

    [Fact]
    public async Task HasRecordsForPrimaryKeyAsync_WithRecords_ReturnsTrue()
    {
        // Arrange
        var (mockFactory, _, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var index = new HistoryIndexData
        {
            EntityId = "entity-1",
            RecordIds = new List<string> { "rec-1" }
        };

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);

        // Act
        var result = await helper.HasRecordsForPrimaryKeyAsync("entity-1");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasRecordsForPrimaryKeyAsync_WithEmptyIndex_ReturnsFalse()
    {
        // Arrange
        var (mockFactory, _, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var index = new HistoryIndexData
        {
            EntityId = "entity-1",
            RecordIds = new List<string>()
        };

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);

        // Act
        var result = await helper.HasRecordsForPrimaryKeyAsync("entity-1");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasRecordsForPrimaryKeyAsync_WithNoIndex_ReturnsFalse()
    {
        // Arrange
        var (mockFactory, _, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Act
        var result = await helper.HasRecordsForPrimaryKeyAsync("entity-1");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetRecordCountByPrimaryKeyAsync_ReturnsCorrectCount()
    {
        // Arrange
        var (mockFactory, _, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var index = new HistoryIndexData
        {
            EntityId = "entity-1",
            RecordIds = new List<string> { "rec-1", "rec-2", "rec-3" }
        };

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);

        // Act
        var result = await helper.GetRecordCountByPrimaryKeyAsync("entity-1");

        // Assert
        Assert.Equal(3, result);
    }

    [Fact]
    public async Task GetRecordCountByPrimaryKeyAsync_WithNoIndex_ReturnsZero()
    {
        // Arrange
        var (mockFactory, _, mockIndexStore) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Act
        var result = await helper.GetRecordCountByPrimaryKeyAsync("entity-1");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetRecordCountByPrimaryKeyAsync_WithEmptyKey_ReturnsZero()
    {
        var (mockFactory, _, _) = CreateMocks();
        var helper = CreateHelper(mockFactory);

        var result = await helper.GetRecordCountByPrimaryKeyAsync("");
        Assert.Equal(0, result);
    }
}
