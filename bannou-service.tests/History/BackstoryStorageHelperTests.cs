using BeyondImmersion.BannouService.History;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Moq;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Tests.History;

/// <summary>
/// Unit tests for BackstoryStorageHelper.
/// </summary>
[Collection("unit tests")]
public class BackstoryStorageHelperTests : IClassFixture<CollectionFixture>
{
    private const string TestStateStoreName = "test-store";
    private const string KeyPrefix = "backstory-";

    private CollectionFixture TestCollectionContext { get; }

    public BackstoryStorageHelperTests(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<BackstoryStorageHelperTests>();
    }

    /// <summary>
    /// Test backstory container.
    /// </summary>
    public class TestBackstory
    {
        public string EntityId { get; set; } = string.Empty;
        public List<TestElement> Elements { get; set; } = new();
        public long CreatedAtUnix { get; set; }
        public long UpdatedAtUnix { get; set; }
    }

    /// <summary>
    /// Test element type.
    /// </summary>
    public class TestElement
    {
        public string ElementType { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public float Strength { get; set; }
    }

    private IBackstoryElementMatcher<TestElement> CreateMatcher()
    {
        return new BackstoryElementMatcher<TestElement>(
            getType: e => e.ElementType,
            getKey: e => e.Key,
            copyValues: (src, dst) =>
            {
                dst.Value = src.Value;
                dst.Strength = src.Strength;
            },
            clone: e => new TestElement
            {
                ElementType = e.ElementType,
                Key = e.Key,
                Value = e.Value,
                Strength = e.Strength
            });
    }

    private BackstoryStorageConfiguration<TestBackstory, TestElement> CreateConfig(Mock<IStateStoreFactory> mockFactory)
    {
        return new BackstoryStorageConfiguration<TestBackstory, TestElement>
        {
            StateStoreFactory = mockFactory.Object,
            StateStoreName = TestStateStoreName,
            KeyPrefix = KeyPrefix,
            ElementMatcher = CreateMatcher(),
            GetEntityId = b => b.EntityId,
            SetEntityId = (b, id) => b.EntityId = id,
            GetElements = b => b.Elements,
            SetElements = (b, e) => b.Elements = e,
            GetCreatedAtUnix = b => b.CreatedAtUnix,
            SetCreatedAtUnix = (b, t) => b.CreatedAtUnix = t,
            GetUpdatedAtUnix = b => b.UpdatedAtUnix,
            SetUpdatedAtUnix = (b, t) => b.UpdatedAtUnix = t
        };
    }

    private (Mock<IStateStoreFactory>, Mock<IStateStore<TestBackstory>>) CreateMocks()
    {
        var mockFactory = new Mock<IStateStoreFactory>();
        var mockStore = new Mock<IStateStore<TestBackstory>>();

        mockFactory.Setup(f => f.GetStore<TestBackstory>(TestStateStoreName)).Returns(mockStore.Object);

        return (mockFactory, mockStore);
    }

    [Fact]
    public void Constructor_WithNullConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BackstoryStorageHelper<TestBackstory, TestElement>(null!));
    }

    [Fact]
    public void Constructor_WithNullFactory_ThrowsArgumentNullException()
    {
        var (mockFactory, _) = CreateMocks();
        var config = CreateConfig(mockFactory) with { StateStoreFactory = null! };

        Assert.Throws<ArgumentNullException>(() =>
            new BackstoryStorageHelper<TestBackstory, TestElement>(config));
    }

    [Fact]
    public async Task GetAsync_WithValidId_ReturnsBackstory()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        var backstory = new TestBackstory
        {
            EntityId = "entity-1",
            Elements = new List<TestElement>
            {
                new() { ElementType = "trait", Key = "brave", Value = "Very brave", Strength = 0.8f }
            },
            CreatedAtUnix = 1000000,
            UpdatedAtUnix = 1000000
        };

        mockStore.Setup(s => s.GetAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(backstory);

        // Act
        var result = await helper.GetAsync("entity-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("entity-1", result.EntityId);
        Assert.Single(result.Elements);
        Assert.Equal("brave", result.Elements[0].Key);
    }

    [Fact]
    public async Task GetAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        mockStore.Setup(s => s.GetAsync($"{KeyPrefix}non-existent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestBackstory?)null);

        // Act
        var result = await helper.GetAsync("non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithEmptyId_ReturnsNull()
    {
        var (mockFactory, _) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        var result = await helper.GetAsync("");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_NewBackstory_CreatesNew()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        mockStore.Setup(s => s.GetAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestBackstory?)null);

        var elements = new List<TestElement>
        {
            new() { ElementType = "trait", Key = "brave", Value = "Very brave", Strength = 0.8f }
        };

        // Act
        var result = await helper.SetAsync("entity-1", elements, replaceExisting: false);

        // Assert
        Assert.True(result.IsNew);
        Assert.Equal("entity-1", result.Backstory.EntityId);
        Assert.Single(result.Backstory.Elements);

        mockStore.Verify(s => s.SaveAsync(
            $"{KeyPrefix}entity-1",
            It.Is<TestBackstory>(b => b.EntityId == "entity-1" && b.Elements.Count == 1),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_ReplaceExisting_ReplacesAllElements()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        var existing = new TestBackstory
        {
            EntityId = "entity-1",
            Elements = new List<TestElement>
            {
                new() { ElementType = "trait", Key = "brave", Value = "Old value", Strength = 0.5f },
                new() { ElementType = "trait", Key = "old", Value = "Old element", Strength = 0.3f }
            },
            CreatedAtUnix = 1000000,
            UpdatedAtUnix = 1000000
        };

        mockStore.Setup(s => s.GetAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var newElements = new List<TestElement>
        {
            new() { ElementType = "trait", Key = "brave", Value = "New value", Strength = 0.9f }
        };

        // Act
        var result = await helper.SetAsync("entity-1", newElements, replaceExisting: true);

        // Assert
        Assert.False(result.IsNew);
        Assert.Single(result.Backstory.Elements);
        Assert.Equal("brave", result.Backstory.Elements[0].Key);
        Assert.Equal("New value", result.Backstory.Elements[0].Value);
        Assert.Equal(0.9f, result.Backstory.Elements[0].Strength);

        // Original CreatedAtUnix should be preserved
        Assert.Equal(1000000, result.Backstory.CreatedAtUnix);
    }

    [Fact]
    public async Task SetAsync_Merge_UpdatesExistingAndAddsNew()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        var existing = new TestBackstory
        {
            EntityId = "entity-1",
            Elements = new List<TestElement>
            {
                new() { ElementType = "trait", Key = "brave", Value = "Old value", Strength = 0.5f }
            },
            CreatedAtUnix = 1000000,
            UpdatedAtUnix = 1000000
        };

        mockStore.Setup(s => s.GetAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var newElements = new List<TestElement>
        {
            new() { ElementType = "trait", Key = "brave", Value = "Updated value", Strength = 0.9f },
            new() { ElementType = "trait", Key = "wise", Value = "New element", Strength = 0.7f }
        };

        // Act
        var result = await helper.SetAsync("entity-1", newElements, replaceExisting: false);

        // Assert
        Assert.False(result.IsNew);
        Assert.Equal(2, result.Backstory.Elements.Count);

        // Existing element should be updated
        var braveElement = result.Backstory.Elements.First(e => e.Key == "brave");
        Assert.Equal("Updated value", braveElement.Value);
        Assert.Equal(0.9f, braveElement.Strength);

        // New element should be added
        var wiseElement = result.Backstory.Elements.First(e => e.Key == "wise");
        Assert.Equal("New element", wiseElement.Value);
        Assert.Equal(0.7f, wiseElement.Strength);
    }

    [Fact]
    public async Task SetAsync_WithEmptyId_ThrowsArgumentNullException()
    {
        var (mockFactory, _) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.SetAsync("", new List<TestElement>(), replaceExisting: false));
    }

    [Fact]
    public async Task SetAsync_WithNullElements_ThrowsArgumentNullException()
    {
        var (mockFactory, _) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.SetAsync("entity-1", null!, replaceExisting: false));
    }

    [Fact]
    public async Task AddElementAsync_NewBackstory_CreatesWithSingleElement()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        mockStore.Setup(s => s.GetAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestBackstory?)null);

        var element = new TestElement { ElementType = "trait", Key = "brave", Value = "Very brave", Strength = 0.8f };

        // Act
        var result = await helper.AddElementAsync("entity-1", element);

        // Assert
        Assert.True(result.IsNew);
        Assert.Single(result.Backstory.Elements);
        Assert.Equal("brave", result.Backstory.Elements[0].Key);
    }

    [Fact]
    public async Task AddElementAsync_ExistingBackstory_AddsNewElement()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        var existing = new TestBackstory
        {
            EntityId = "entity-1",
            Elements = new List<TestElement>
            {
                new() { ElementType = "trait", Key = "brave", Value = "Old value", Strength = 0.5f }
            },
            CreatedAtUnix = 1000000,
            UpdatedAtUnix = 1000000
        };

        mockStore.Setup(s => s.GetAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var element = new TestElement { ElementType = "trait", Key = "wise", Value = "New element", Strength = 0.7f };

        // Act
        var result = await helper.AddElementAsync("entity-1", element);

        // Assert
        Assert.False(result.IsNew);
        Assert.Equal(2, result.Backstory.Elements.Count);
    }

    [Fact]
    public async Task AddElementAsync_ExistingElement_UpdatesIt()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        var existing = new TestBackstory
        {
            EntityId = "entity-1",
            Elements = new List<TestElement>
            {
                new() { ElementType = "trait", Key = "brave", Value = "Old value", Strength = 0.5f }
            },
            CreatedAtUnix = 1000000,
            UpdatedAtUnix = 1000000
        };

        mockStore.Setup(s => s.GetAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var element = new TestElement { ElementType = "trait", Key = "brave", Value = "Updated value", Strength = 0.9f };

        // Act
        var result = await helper.AddElementAsync("entity-1", element);

        // Assert
        Assert.False(result.IsNew);
        Assert.Single(result.Backstory.Elements);
        Assert.Equal("Updated value", result.Backstory.Elements[0].Value);
        Assert.Equal(0.9f, result.Backstory.Elements[0].Strength);
    }

    [Fact]
    public async Task AddElementAsync_WithEmptyId_ThrowsArgumentNullException()
    {
        var (mockFactory, _) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.AddElementAsync("", new TestElement()));
    }

    [Fact]
    public async Task AddElementAsync_WithNullElement_ThrowsArgumentNullException()
    {
        var (mockFactory, _) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            helper.AddElementAsync("entity-1", null!));
    }

    [Fact]
    public async Task DeleteAsync_ExistingBackstory_ReturnsTrue()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        var existing = new TestBackstory { EntityId = "entity-1" };
        mockStore.Setup(s => s.GetAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        mockStore.Setup(s => s.DeleteAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await helper.DeleteAsync("entity-1");

        // Assert
        Assert.True(result);
        mockStore.Verify(s => s.DeleteAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentBackstory_ReturnsFalse()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        mockStore.Setup(s => s.GetAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestBackstory?)null);

        // Act
        var result = await helper.DeleteAsync("entity-1");

        // Assert
        Assert.False(result);
        mockStore.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WithEmptyId_ReturnsFalse()
    {
        var (mockFactory, _) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        var result = await helper.DeleteAsync("");
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_ExistingBackstory_ReturnsTrue()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        mockStore.Setup(s => s.ExistsAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await helper.ExistsAsync("entity-1");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_NonExistentBackstory_ReturnsFalse()
    {
        // Arrange
        var (mockFactory, mockStore) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        mockStore.Setup(s => s.ExistsAsync($"{KeyPrefix}entity-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await helper.ExistsAsync("entity-1");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_WithEmptyId_ReturnsFalse()
    {
        var (mockFactory, _) = CreateMocks();
        var helper = new BackstoryStorageHelper<TestBackstory, TestElement>(CreateConfig(mockFactory));

        var result = await helper.ExistsAsync("");
        Assert.False(result);
    }

    [Fact]
    public void BackstoryElementMatcher_GetElementType_ReturnsType()
    {
        var matcher = CreateMatcher();
        var element = new TestElement { ElementType = "trait", Key = "brave" };

        Assert.Equal("trait", matcher.GetElementType(element));
    }

    [Fact]
    public void BackstoryElementMatcher_GetElementKey_ReturnsKey()
    {
        var matcher = CreateMatcher();
        var element = new TestElement { ElementType = "trait", Key = "brave" };

        Assert.Equal("brave", matcher.GetElementKey(element));
    }

    [Fact]
    public void BackstoryElementMatcher_CopyValues_CopiesNonIdentityFields()
    {
        var matcher = CreateMatcher();
        var source = new TestElement { ElementType = "trait", Key = "brave", Value = "New value", Strength = 0.9f };
        var target = new TestElement { ElementType = "trait", Key = "brave", Value = "Old value", Strength = 0.1f };

        matcher.CopyValues(source, target);

        Assert.Equal("New value", target.Value);
        Assert.Equal(0.9f, target.Strength);
        // Identity fields should remain unchanged
        Assert.Equal("trait", target.ElementType);
        Assert.Equal("brave", target.Key);
    }

    [Fact]
    public void BackstoryElementMatcher_Clone_CreatesIndependentCopy()
    {
        var matcher = CreateMatcher();
        var original = new TestElement { ElementType = "trait", Key = "brave", Value = "Value", Strength = 0.5f };

        var clone = matcher.Clone(original);

        Assert.NotSame(original, clone);
        Assert.Equal(original.ElementType, clone.ElementType);
        Assert.Equal(original.Key, clone.Key);
        Assert.Equal(original.Value, clone.Value);
        Assert.Equal(original.Strength, clone.Strength);

        // Modifying clone should not affect original
        clone.Value = "Modified";
        Assert.Equal("Value", original.Value);
    }

    [Fact]
    public void BackstoryElementMatcher_Constructor_WithNullGetType_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new BackstoryElementMatcher<TestElement>(
            null!,
            e => e.Key,
            (s, t) => { },
            e => new TestElement()));
    }
}
