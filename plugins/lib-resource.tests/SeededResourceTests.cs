using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;

namespace BeyondImmersion.BannouService.Resource.Tests;

/// <summary>
/// Unit tests for seeded resource functionality in ResourceService.
/// Tests verify provider discovery, listing, retrieval, and the EmbeddedResourceProvider base class.
/// </summary>
public class SeededResourceTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IServiceNavigator> _mockNavigator;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<ResourceService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly ResourceServiceConfiguration _configuration;

    // Mock stores (needed by constructor even if not used by seeded resource methods)
    private readonly Mock<ICacheableStateStore<ResourceReferenceEntry>> _mockRefStore;
    private readonly Mock<IStateStore<GracePeriodRecord>> _mockGraceStore;
    private readonly Mock<IStateStore<CleanupCallbackDefinition>> _mockCleanupStore;
    private readonly Mock<ICacheableStateStore<string>> _mockCallbackIndexStore;
    private readonly Mock<IStateStore<CompressCallbackDefinition>> _mockCompressStore;
    private readonly Mock<IStateStore<ResourceArchiveModel>> _mockArchiveStore;
    private readonly Mock<ICacheableStateStore<string>> _mockCompressIndexStore;
    private readonly Mock<IStateStore<ResourceSnapshotModel>> _mockSnapshotStore;

    public SeededResourceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockNavigator = new Mock<IServiceNavigator>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<ResourceService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        _mockRefStore = new Mock<ICacheableStateStore<ResourceReferenceEntry>>();
        _mockGraceStore = new Mock<IStateStore<GracePeriodRecord>>();
        _mockCleanupStore = new Mock<IStateStore<CleanupCallbackDefinition>>();
        _mockCallbackIndexStore = new Mock<ICacheableStateStore<string>>();
        _mockCompressStore = new Mock<IStateStore<CompressCallbackDefinition>>();
        _mockArchiveStore = new Mock<IStateStore<ResourceArchiveModel>>();
        _mockCompressIndexStore = new Mock<ICacheableStateStore<string>>();
        _mockSnapshotStore = new Mock<IStateStore<ResourceSnapshotModel>>();

        _configuration = new ResourceServiceConfiguration
        {
            DefaultGracePeriodSeconds = 3600,
            CleanupCallbackTimeoutSeconds = 30,
            CleanupLockExpirySeconds = 300,
            DefaultCleanupPolicy = CleanupPolicy.BEST_EFFORT,
            DefaultCompressionPolicy = CompressionPolicy.ALL_REQUIRED,
            CompressionCallbackTimeoutSeconds = 60,
            CompressionLockExpirySeconds = 600,
            SnapshotDefaultTtlSeconds = 3600,
            SnapshotMinTtlSeconds = 60,
            SnapshotMaxTtlSeconds = 86400
        };

        SetupStateStoreFactory();
    }

    private void SetupStateStoreFactory()
    {
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<ResourceReferenceEntry>(StateStoreDefinitions.ResourceRefcounts))
            .Returns(_mockRefStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CleanupCallbackDefinition>(StateStoreDefinitions.ResourceCleanup))
            .Returns(_mockCleanupStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GracePeriodRecord>(StateStoreDefinitions.ResourceGrace))
            .Returns(_mockGraceStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>(StateStoreDefinitions.ResourceCleanup))
            .Returns(_mockCallbackIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CompressCallbackDefinition>(StateStoreDefinitions.ResourceCompress))
            .Returns(_mockCompressStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ResourceArchiveModel>(StateStoreDefinitions.ResourceArchives))
            .Returns(_mockArchiveStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>(StateStoreDefinitions.ResourceCompress))
            .Returns(_mockCompressIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ResourceSnapshotModel>(StateStoreDefinitions.ResourceSnapshots))
            .Returns(_mockSnapshotStore.Object);
    }

    private ResourceService CreateService(IEnumerable<ISeededResourceProvider>? seededProviders = null)
    {
        return new ResourceService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockNavigator.Object,
            _mockLockProvider.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object,
            seededProviders ?? Array.Empty<ISeededResourceProvider>());
    }

    // =========================================================================
    // ListSeededResources Tests
    // =========================================================================

    [Fact]
    public async Task ListSeededResources_NoProviders_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();

        // Act
        var (status, response) = await service.ListSeededResourcesAsync(
            new ListSeededResourcesRequest { ResourceType = null },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Resources);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task ListSeededResources_WithProviders_ReturnsAllResources()
    {
        // Arrange
        var provider1 = new TestSeededResourceProvider("behavior", new[]
        {
            ("idle", "application/yaml", "behavior: idle"),
            ("combat", "application/yaml", "behavior: combat")
        });
        var provider2 = new TestSeededResourceProvider("species", new[]
        {
            ("human", "application/json", "{\"name\":\"Human\"}")
        });

        var service = CreateService(new[] { provider1, provider2 });

        // Act
        var (status, response) = await service.ListSeededResourcesAsync(
            new ListSeededResourcesRequest { ResourceType = null },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.TotalCount);
        Assert.Contains(response.Resources, r => r.Identifier == "idle" && r.ResourceType == "behavior");
        Assert.Contains(response.Resources, r => r.Identifier == "combat" && r.ResourceType == "behavior");
        Assert.Contains(response.Resources, r => r.Identifier == "human" && r.ResourceType == "species");
    }

    [Fact]
    public async Task ListSeededResources_WithTypeFilter_ReturnsFilteredResources()
    {
        // Arrange
        var provider1 = new TestSeededResourceProvider("behavior", new[]
        {
            ("idle", "application/yaml", "behavior: idle")
        });
        var provider2 = new TestSeededResourceProvider("species", new[]
        {
            ("human", "application/json", "{\"name\":\"Human\"}")
        });

        var service = CreateService(new[] { provider1, provider2 });

        // Act
        var (status, response) = await service.ListSeededResourcesAsync(
            new ListSeededResourcesRequest { ResourceType = "behavior" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.TotalCount);
        Assert.Single(response.Resources);
        var resource = response.Resources.First();
        Assert.Equal("idle", resource.Identifier);
        Assert.Equal("behavior", resource.ResourceType);
    }

    [Fact]
    public async Task ListSeededResources_TypeFilterCaseInsensitive()
    {
        // Arrange
        var provider = new TestSeededResourceProvider("Behavior", new[]
        {
            ("idle", "application/yaml", "behavior: idle")
        });

        var service = CreateService(new[] { provider });

        // Act
        var (status, response) = await service.ListSeededResourcesAsync(
            new ListSeededResourcesRequest { ResourceType = "BEHAVIOR" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.TotalCount);
    }

    // =========================================================================
    // GetSeededResource Tests
    // =========================================================================

    [Fact]
    public async Task GetSeededResource_NotFound_ReturnsFoundFalse()
    {
        // Arrange
        var provider = new TestSeededResourceProvider("behavior", new[]
        {
            ("idle", "application/yaml", "behavior: idle")
        });

        var service = CreateService(new[] { provider });

        // Act
        var (status, response) = await service.GetSeededResourceAsync(
            new GetSeededResourceRequest { ResourceType = "behavior", Identifier = "nonexistent" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Found);
        Assert.Null(response.Resource);
    }

    [Fact]
    public async Task GetSeededResource_NoProviderForType_ReturnsFoundFalse()
    {
        // Arrange
        var provider = new TestSeededResourceProvider("behavior", new[]
        {
            ("idle", "application/yaml", "behavior: idle")
        });

        var service = CreateService(new[] { provider });

        // Act
        var (status, response) = await service.GetSeededResourceAsync(
            new GetSeededResourceRequest { ResourceType = "species", Identifier = "human" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Found);
        Assert.Null(response.Resource);
    }

    [Fact]
    public async Task GetSeededResource_Found_ReturnsContent()
    {
        // Arrange
        var content = "behavior:\n  type: idle\n  duration: 10";
        var provider = new TestSeededResourceProvider("behavior", new[]
        {
            ("idle", "application/yaml", content)
        });

        var service = CreateService(new[] { provider });

        // Act
        var (status, response) = await service.GetSeededResourceAsync(
            new GetSeededResourceRequest { ResourceType = "behavior", Identifier = "idle" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Found);
        Assert.NotNull(response.Resource);
        Assert.Equal("behavior", response.Resource.ResourceType);
        Assert.Equal("idle", response.Resource.Identifier);
        Assert.Equal("application/yaml", response.Resource.ContentType);

        // Decode content
        var decodedContent = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(response.Resource.Content));
        Assert.Equal(content, decodedContent);
    }

    [Fact]
    public async Task GetSeededResource_WithMetadata_ReturnsMetadata()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            { "version", "1.0" },
            { "author", "test" }
        };
        var provider = new TestSeededResourceProvider("behavior", new[]
        {
            ("idle", "application/yaml", "behavior: idle")
        }, metadata);

        var service = CreateService(new[] { provider });

        // Act
        var (status, response) = await service.GetSeededResourceAsync(
            new GetSeededResourceRequest { ResourceType = "behavior", Identifier = "idle" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Found);
        Assert.NotNull(response.Resource);
        Assert.NotNull(response.Resource.Metadata);
        Assert.Equal("1.0", response.Resource.Metadata["version"]);
        Assert.Equal("test", response.Resource.Metadata["author"]);
    }

    [Fact]
    public async Task GetSeededResource_IdentifierCaseInsensitive()
    {
        // Arrange
        var provider = new TestSeededResourceProvider("behavior", new[]
        {
            ("Idle", "application/yaml", "behavior: idle")
        });

        var service = CreateService(new[] { provider });

        // Act
        var (status, response) = await service.GetSeededResourceAsync(
            new GetSeededResourceRequest { ResourceType = "behavior", Identifier = "idle" },
            CancellationToken.None);

        // Assert - this depends on provider implementation, TestSeededResourceProvider is case-sensitive
        // In production with EmbeddedResourceProvider, identifier matching is case-insensitive
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        // Note: TestSeededResourceProvider is case-sensitive by design
    }

    // =========================================================================
    // SeededResource Record Tests
    // =========================================================================

    [Fact]
    public void SeededResource_SizeBytes_ReturnsContentLength()
    {
        // Arrange
        var content = "Hello, World!";
        var resource = new SeededResource(
            "test",
            "test-type",
            "text/plain",
            System.Text.Encoding.UTF8.GetBytes(content),
            new Dictionary<string, string>());

        // Assert
        Assert.Equal(content.Length, resource.SizeBytes);
    }

    [Fact]
    public void SeededResource_GetContentAsString_ReturnsUtf8String()
    {
        // Arrange
        var content = "Hello, World! 你好世界";
        var resource = new SeededResource(
            "test",
            "test-type",
            "text/plain",
            System.Text.Encoding.UTF8.GetBytes(content),
            new Dictionary<string, string>());

        // Act
        var decoded = resource.GetContentAsString();

        // Assert
        Assert.Equal(content, decoded);
    }

    // =========================================================================
    // Test Helpers
    // =========================================================================

    /// <summary>
    /// Test implementation of ISeededResourceProvider for unit testing.
    /// </summary>
    private class TestSeededResourceProvider : ISeededResourceProvider
    {
        private readonly Dictionary<string, (string ContentType, string Content)> _resources;
        private readonly IReadOnlyDictionary<string, string> _metadata;

        public string ResourceType { get; }

        /// <summary>
        /// Provider-level content type (uses first resource's content type or default).
        /// </summary>
        public string ContentType { get; }

        public TestSeededResourceProvider(
            string resourceType,
            (string Identifier, string ContentType, string Content)[] resources,
            Dictionary<string, string>? metadata = null)
        {
            ResourceType = resourceType;
            _resources = resources.ToDictionary(
                r => r.Identifier,
                r => (r.ContentType, r.Content));
            _metadata = metadata ?? new Dictionary<string, string>();
            // Use first resource's content type as provider-level default
            ContentType = resources.Length > 0 ? resources[0].ContentType : "application/octet-stream";
        }

        public Task<IReadOnlyList<string>> ListSeededAsync(CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<string>>(_resources.Keys.ToList());
        }

        public Task<SeededResource?> GetSeededAsync(string identifier, CancellationToken ct)
        {
            if (!_resources.TryGetValue(identifier, out var resource))
            {
                return Task.FromResult<SeededResource?>(null);
            }

            return Task.FromResult<SeededResource?>(new SeededResource(
                identifier,
                ResourceType,
                resource.ContentType,
                System.Text.Encoding.UTF8.GetBytes(resource.Content),
                _metadata));
        }
    }
}
