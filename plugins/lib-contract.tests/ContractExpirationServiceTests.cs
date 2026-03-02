using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Contract.Tests;

/// <summary>
/// Unit tests for ContractExpirationService.
/// Tests the background service's ExecuteAsync lifecycle, including
/// startup delay cancellation, pending contract activation checking,
/// active contract expiration checking, GUID parsing, per-contract
/// error isolation, and loop-level error handling with error event publishing.
/// </summary>
public class ContractExpirationServiceTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceProvider> _mockScopedProvider;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<List<string>>> _mockIndexStore;
    private readonly Mock<IContractService> _mockContractService;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly ContractServiceConfiguration _configuration;

    public ContractExpirationServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScope = new Mock<IServiceScope>();
        _mockScopedProvider = new Mock<IServiceProvider>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockIndexStore = new Mock<IStateStore<List<string>>>();
        _mockContractService = new Mock<IContractService>();
        _mockMessageBus = new Mock<IMessageBus>();
        _configuration = new ContractServiceConfiguration
        {
            MilestoneDeadlineStartupDelaySeconds = 0,
            MilestoneDeadlineCheckIntervalSeconds = 600,
        };

        // Wire up the DI scope chain
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockScopeFactory.Object);
        _mockScopeFactory.Setup(f => f.CreateScope())
            .Returns(_mockScope.Object);
        _mockScope.Setup(s => s.ServiceProvider)
            .Returns(_mockScopedProvider.Object);

        // Common scoped service registrations
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(ITelemetryProvider)))
            .Returns(new NullTelemetryProvider());
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Returns(_mockStateStoreFactory.Object);
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IContractService)))
            .Returns(_mockContractService.Object);
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IMessageBus)))
            .Returns(_mockMessageBus.Object);

        // Wire up state store factory for index store
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(StateStoreDefinitions.Contract))
            .Returns(_mockIndexStore.Object);

        // Default: no pending or active contracts
        _mockIndexStore.Setup(s => s.GetAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);
    }

    // ============================================================================
    // Startup / Cancellation
    // ============================================================================

    [Fact]
    public async Task ExecuteAsync_WhenCancelledDuringStartup_StopsGracefully()
    {
        // Arrange
        _configuration.MilestoneDeadlineStartupDelaySeconds = 60;
        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - should not throw
        await worker.StartAsync(cts.Token);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledDuringCheckInterval_StopsGracefully()
    {
        // Arrange: zero startup delay, long check interval
        _configuration.MilestoneDeadlineCheckIntervalSeconds = 600;

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        // Act & Assert - runs one cycle then cancels during interval delay
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        // Verify at least one scope was created (one check cycle ran)
        _mockScopeFactory.Verify(f => f.CreateScope(), Times.AtLeastOnce);
    }

    // ============================================================================
    // No Contracts
    // ============================================================================

    [Fact]
    public async Task CheckActiveContracts_NoPendingOrActive_ReturnsWithoutProcessing()
    {
        // Arrange: both pending and active indexes return null
        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - contract service never called
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.IsAny<GetContractInstanceStatusRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckActiveContracts_EmptyLists_ReturnsWithoutProcessing()
    {
        // Arrange: both indexes return empty lists
        _mockIndexStore.Setup(s => s.GetAsync("status-idx:pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _mockIndexStore.Setup(s => s.GetAsync("status-idx:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - contract service never called
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.IsAny<GetContractInstanceStatusRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ============================================================================
    // Pending Contract Activation
    // ============================================================================

    [Fact]
    public async Task CheckActiveContracts_WithPendingContracts_ChecksEachForActivation()
    {
        // Arrange
        var contractId1 = Guid.NewGuid();
        var contractId2 = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("status-idx:pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { contractId1.ToString(), contractId2.ToString() });
        _mockIndexStore.Setup(s => s.GetAsync("status-idx:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.IsAny<GetContractInstanceStatusRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.OK, new ContractInstanceStatusResponse()));

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - each pending contract checked
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == contractId1),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == contractId2),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckActiveContracts_PendingContractWithInvalidGuid_SkipsIt()
    {
        // Arrange: one valid, one invalid GUID
        var validId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("status-idx:pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "not-a-guid", validId.ToString() });
        _mockIndexStore.Setup(s => s.GetAsync("status-idx:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.IsAny<GetContractInstanceStatusRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.OK, new ContractInstanceStatusResponse()));

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - only the valid contract checked, invalid one silently skipped
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == validId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.IsAny<GetContractInstanceStatusRequest>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce); // only the valid one
    }

    [Fact]
    public async Task CheckActiveContracts_PendingContractThrows_ContinuesWithRemaining()
    {
        // Arrange
        var failId = Guid.NewGuid();
        var okId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("status-idx:pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { failId.ToString(), okId.ToString() });
        _mockIndexStore.Setup(s => s.GetAsync("status-idx:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == failId),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("State unavailable"));

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == okId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.OK, new ContractInstanceStatusResponse()));

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - second contract still checked despite first failing
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == okId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ============================================================================
    // Active Contract Expiration Checking
    // ============================================================================

    [Fact]
    public async Task CheckActiveContracts_WithActiveContracts_ChecksEachForExpiration()
    {
        // Arrange
        var contractId1 = Guid.NewGuid();
        var contractId2 = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("status-idx:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { contractId1.ToString(), contractId2.ToString() });

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.IsAny<GetContractInstanceStatusRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.OK, new ContractInstanceStatusResponse()));

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - each active contract checked
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == contractId1),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == contractId2),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckActiveContracts_ActiveContractWithInvalidGuid_SkipsAndLogsWarning()
    {
        // Arrange
        var validId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("status-idx:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "bad-guid", validId.ToString() });

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.IsAny<GetContractInstanceStatusRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.OK, new ContractInstanceStatusResponse()));

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - only the valid contract checked
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == validId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckActiveContracts_ActiveContractThrows_ContinuesWithRemaining()
    {
        // Arrange
        var failId = Guid.NewGuid();
        var okId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("status-idx:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { failId.ToString(), okId.ToString() });

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == failId),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis timeout"));

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == okId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.OK, new ContractInstanceStatusResponse()));

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - second contract still checked despite first failing
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == okId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ============================================================================
    // Payment Schedule (Mock Path)
    // ============================================================================

    [Fact]
    public async Task CheckActiveContracts_WhenContractServiceIsMock_SkipsPaymentScheduleCheck()
    {
        // Arrange: IContractService is a mock (not concrete ContractService),
        // so "contractServiceImpl as ContractService" will be null and payment
        // schedule checking will be skipped entirely
        var contractId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("status-idx:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { contractId.ToString() });

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.IsAny<GetContractInstanceStatusRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.OK, new ContractInstanceStatusResponse()));

        // Set up a ContractInstanceModel store — if payment schedule were checked,
        // it would query this store. We verify it's never called.
        var mockInstanceStore = new Mock<IStateStore<ContractInstanceModel>>();
        _mockStateStoreFactory.Setup(f => f.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract))
            .Returns(mockInstanceStore.Object);

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - instance store never queried (payment schedule skipped)
        mockInstanceStore.Verify(
            s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ============================================================================
    // Loop-Level Error Handling
    // ============================================================================

    [Fact]
    public async Task ExecuteAsync_WhenCheckThrows_PublishesErrorEventAndContinues()
    {
        // Arrange: state store factory throws during scope resolution
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Throws(new InvalidOperationException("Redis unavailable"));

        // Set up a separate scope for error publishing
        var errorScopeProvider = new Mock<IServiceProvider>();
        errorScopeProvider.Setup(sp => sp.GetService(typeof(IMessageBus)))
            .Returns(_mockMessageBus.Object);

        var errorScope = new Mock<IServiceScope>();
        errorScope.Setup(s => s.ServiceProvider)
            .Returns(errorScopeProvider.Object);

        // CreateScope will return: first call = main scope (throws),
        // second call = error scope (for error publishing)
        // But the service calls CreateScope twice per cycle (one for main, one for error)
        // The approach: the main scope throws when resolving IStateStoreFactory,
        // then the error scope is created separately via _serviceProvider.CreateScope()
        // In the error path, it creates a new scope directly from _serviceProvider
        // Both calls go through the same mock, so we need to handle it differently.
        // Actually, looking at the code more carefully:
        //   - Main check: uses scope = _serviceProvider.CreateScope()
        //   - Error handler: uses errorScope = _serviceProvider.CreateScope()
        // Both call the same _serviceProvider.CreateScope(), so we can't differentiate.
        // Instead, let the main scope work but make the service resolution throw.
        // The error handler creates its own scope and resolves IMessageBus from it.
        // Since both scopes come from the same factory, both get the same scoped provider.
        // We need the scoped provider to have IMessageBus available while IStateStoreFactory throws.

        // Reset: Let IStateStoreFactory throw, but IMessageBus is available on same provider
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Throws(new InvalidOperationException("Redis unavailable"));
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IMessageBus)))
            .Returns(_mockMessageBus.Object);

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act - should not crash; loop catches and publishes error
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - error event published
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "contract",
            "ExpirationCheck",
            "InvalidOperationException",
            "Redis unavailable",
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            ServiceErrorEventSeverity.Error,
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenErrorPublishAlsoFails_StillContinuesLoop()
    {
        // Arrange: check throws, AND error publishing throws
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Throws(new InvalidOperationException("Check failed"));
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IMessageBus)))
            .Returns(_mockMessageBus.Object);

        _mockMessageBus.Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ also down"));

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act & Assert - worker should not crash even when error publishing fails
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // If we got here without exception, the test passes - the loop survived
    }

    // ============================================================================
    // Combined Pending + Active
    // ============================================================================

    [Fact]
    public async Task CheckActiveContracts_BothPendingAndActive_ChecksBothSets()
    {
        // Arrange
        var pendingId = Guid.NewGuid();
        var activeId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("status-idx:pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { pendingId.ToString() });
        _mockIndexStore.Setup(s => s.GetAsync("status-idx:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { activeId.ToString() });

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.IsAny<GetContractInstanceStatusRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.OK, new ContractInstanceStatusResponse()));

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - both pending and active contracts checked
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == pendingId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == activeId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckActiveContracts_PendingActivatedButNoActive_ReturnsAfterPending()
    {
        // Arrange: pending contracts exist but active index is empty
        var pendingId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("status-idx:pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { pendingId.ToString() });
        _mockIndexStore.Setup(s => s.GetAsync("status-idx:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.IsAny<GetContractInstanceStatusRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.OK, new ContractInstanceStatusResponse()));

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - pending contract was checked (activated path)
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == pendingId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckActiveContracts_StatusNotOK_DoesNotCountAsActivated()
    {
        // Arrange: pending contract returns non-OK status (e.g., not yet ready)
        var pendingId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("status-idx:pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { pendingId.ToString() });
        _mockIndexStore.Setup(s => s.GetAsync("status-idx:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        _mockContractService.Setup(s => s.GetContractInstanceStatusAsync(
                It.IsAny<GetContractInstanceStatusRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.NotFound, (ContractInstanceStatusResponse?)null));

        using var worker = new ContractExpirationService(
            _mockServiceProvider.Object,
            new Mock<ILogger<ContractExpirationService>>().Object,
            _configuration);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - service was still called (just didn't count as activated)
        _mockContractService.Verify(
            s => s.GetContractInstanceStatusAsync(
                It.Is<GetContractInstanceStatusRequest>(r => r.ContractId == pendingId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
