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
        Program.Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<DualIndexHelperTests>.Instance;
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

    private (Mock<IStateStoreFactory>, Mock<IStateStore<TestRecord>>, Mock<IStateStore<HistoryIndexData>>, Mock<IDistributedLockProvider>) CreateMocks()
    {
        var mockFactory = new Mock<IStateStoreFactory>();
        var mockRecordStore = new Mock<IStateStore<TestRecord>>();
        var mockIndexStore = new Mock<IStateStore<HistoryIndexData>>();
        var mockLockProvider = new Mock<IDistributedLockProvider>();

        mockFactory.Setup(f => f.GetStore<TestRecord>(TestStateStoreName)).Returns(mockRecordStore.Object);
        mockFactory.Setup(f => f.GetStore<HistoryIndexData>(TestStateStoreName)).Returns(mockIndexStore.Object);

        // Default: lock provider succeeds
        var successLock = new Mock<ILockResponse>();
        successLock.Setup(l => l.Success).Returns(true);
        mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);

        return (mockFactory, mockRecordStore, mockIndexStore, mockLockProvider);
    }

    private DualIndexHelper<TestRecord> CreateHelper(Mock<IStateStoreFactory> mockFactory, Mock<IDistributedLockProvider> mockLockProvider)
    {
        return new DualIndexHelper<TestRecord>(
            mockFactory.Object,
            TestStateStoreName,
            RecordPrefix,
            PrimaryIndexPrefix,
            SecondaryIndexPrefix,
            mockLockProvider.Object,
            15);
    }

    [Fact]
    public void Constructor_WithConfiguration_SetsPropertiesCorrectly()
    {
        var (mockFactory, _, _, mockLockProvider) = CreateMocks();
        var config = new DualIndexConfiguration(
            mockFactory.Object,
            TestStateStoreName,
            RecordPrefix,
            PrimaryIndexPrefix,
            SecondaryIndexPrefix,
            mockLockProvider.Object,
            15);

        var helper = new DualIndexHelper<TestRecord>(config);
        Assert.NotNull(helper);
    }

    [Fact]
    public async Task AddRecordAsync_SavesRecordAndUpdatesIndices()
    {
        // Arrange
        var (mockFactory, mockRecordStore, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

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
        Assert.True(result.LockAcquired);
        Assert.Equal(recordId, result.Value);

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
        var (mockFactory, mockRecordStore, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

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
    public async Task AddRecordAsync_WithEmptyRecordId_ThrowsArgumentNullException()
    {
        var (mockFactory, _, _, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.AddRecordAsync(new TestRecord(), "", "pk", "sk"));
    }

    [Fact]
    public async Task GetRecordAsync_WithValidId_ReturnsRecord()
    {
        // Arrange
        var (mockFactory, mockRecordStore, _, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

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
        var (mockFactory, mockRecordStore, _, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

        mockRecordStore.Setup(s => s.GetAsync($"{RecordPrefix}non-existent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestRecord?)null);

        // Act
        var result = await helper.GetRecordAsync("non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecordAsync_WithEmptyId_ThrowsArgumentNullException()
    {
        var (mockFactory, _, _, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.GetRecordAsync(""));
    }

    [Fact]
    public async Task GetRecordIdsByPrimaryKeyAsync_ReturnsRecordIds()
    {
        // Arrange
        var (mockFactory, _, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

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
        var (mockFactory, _, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Act
        var result = await helper.GetRecordIdsByPrimaryKeyAsync("entity-1");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecordIdsByPrimaryKeyAsync_WithEmptyKey_ThrowsArgumentNullException()
    {
        var (mockFactory, _, _, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.GetRecordIdsByPrimaryKeyAsync(""));
    }

    [Fact]
    public async Task GetRecordIdsBySecondaryKeyAsync_ReturnsRecordIds()
    {
        // Arrange
        var (mockFactory, _, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

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
        var (mockFactory, mockRecordStore, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

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
        var (mockFactory, mockRecordStore, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

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
        Assert.True(result.LockAcquired);
        Assert.True(result.Value);
        mockRecordStore.Verify(s => s.DeleteAsync(recordKey, It.IsAny<CancellationToken>()), Times.Once);

        // Verify indices were updated (rec-1 should be removed)
        mockIndexStore.Verify(s => s.SaveAsync(
            $"{PrimaryIndexPrefix}{primaryKey}",
            It.Is<HistoryIndexData>(i => !i.RecordIds.Contains("rec-1") && i.RecordIds.Contains("rec-2")),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Secondary index had only one record; removing it triggers deletion instead of save
        mockIndexStore.Verify(s => s.DeleteAsync(
            $"{SecondaryIndexPrefix}{secondaryKey}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveRecordAsync_WithNonExistentRecord_ReturnsFalse()
    {
        // Arrange
        var (mockFactory, mockRecordStore, _, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

        mockRecordStore.Setup(s => s.GetAsync($"{RecordPrefix}rec-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestRecord?)null);

        // Act
        var result = await helper.RemoveRecordAsync("rec-1", "entity-1", "event-1");

        // Assert
        Assert.True(result.LockAcquired);
        Assert.False(result.Value);
        mockRecordStore.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveRecordAsync_WithEmptyId_ThrowsArgumentNullException()
    {
        var (mockFactory, _, _, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.RemoveRecordAsync("", "entity-1", "event-1"));
    }

    [Fact]
    public async Task RemoveAllByPrimaryKeyAsync_DeletesAllRecordsAndUpdatesIndices()
    {
        // Arrange
        var (mockFactory, mockRecordStore, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

        var primaryKey = "entity-1";
        var primaryIndex = new HistoryIndexData
        {
            EntityId = primaryKey,
            RecordIds = new List<string> { "rec-1", "rec-2" }
        };

        var record1 = new TestRecord { Id = "rec-1", EventId = "event-1" };
        var record2 = new TestRecord { Id = "rec-2", EventId = "event-2" };

        // Mock primary index lookup
        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}{primaryKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(primaryIndex);

        // Mock bulk get for records (new optimized implementation)
        mockRecordStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> keys, CancellationToken _) =>
            {
                var result = new Dictionary<string, TestRecord>();
                foreach (var key in keys)
                {
                    if (key == $"{RecordPrefix}rec-1") result[key] = record1;
                    if (key == $"{RecordPrefix}rec-2") result[key] = record2;
                }
                return result;
            });

        var event1Index = new HistoryIndexData { EntityId = "event-1", RecordIds = new List<string> { "rec-1" } };
        var event2Index = new HistoryIndexData { EntityId = "event-2", RecordIds = new List<string> { "rec-2" } };

        // Mock bulk get for secondary indices
        mockIndexStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> keys, CancellationToken _) =>
            {
                var result = new Dictionary<string, HistoryIndexData>();
                foreach (var key in keys)
                {
                    if (key == $"{SecondaryIndexPrefix}event-1") result[key] = event1Index;
                    if (key == $"{SecondaryIndexPrefix}event-2") result[key] = event2Index;
                }
                return result;
            });

        // Mock bulk save for secondary indices
        mockIndexStore.Setup(s => s.SaveBulkAsync(It.IsAny<IEnumerable<KeyValuePair<string, HistoryIndexData>>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Mock bulk delete for records
        mockRecordStore.Setup(s => s.DeleteBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> keys, CancellationToken _) => keys.Count());

        // Act
        var result = await helper.RemoveAllByPrimaryKeyAsync(primaryKey, r => r.EventId);

        // Assert
        Assert.True(result.LockAcquired);
        Assert.Equal(2, result.Value);
        mockRecordStore.Verify(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        mockRecordStore.Verify(s => s.DeleteBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        mockIndexStore.Verify(s => s.DeleteAsync($"{PrimaryIndexPrefix}{primaryKey}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAllByPrimaryKeyAsync_WithEmptyKey_ThrowsArgumentNullException()
    {
        var (mockFactory, _, _, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.RemoveAllByPrimaryKeyAsync("", r => r.EventId));
    }

    [Fact]
    public async Task HasRecordsForPrimaryKeyAsync_WithRecords_ReturnsTrue()
    {
        // Arrange
        var (mockFactory, _, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

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
        var (mockFactory, _, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

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
        var (mockFactory, _, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

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
        var (mockFactory, _, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

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
        var (mockFactory, _, mockIndexStore, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

        mockIndexStore.Setup(s => s.GetAsync($"{PrimaryIndexPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Act
        var result = await helper.GetRecordCountByPrimaryKeyAsync("entity-1");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetRecordCountByPrimaryKeyAsync_WithEmptyKey_ThrowsArgumentNullException()
    {
        var (mockFactory, _, _, mockLockProvider) = CreateMocks();
        var helper = CreateHelper(mockFactory, mockLockProvider);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.GetRecordCountByPrimaryKeyAsync(""));
    }
}
