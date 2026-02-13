using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.Bannou.BehaviorExpressions.Runtime;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;

namespace BeyondImmersion.BannouService.Actor.Tests;

/// <summary>
/// Unit tests for cognition pipeline wiring in ActorRunner.
/// Tests template resolution order, override composition, flow resolution,
/// and two-phase tick execution (cognition then behavior).
/// </summary>
public class ActorRunnerCognitionTests
{
    private static ActorTemplateData CreateTestTemplate(
        string category = "npc-brain",
        string? cognitionTemplateId = null,
        CognitionOverrides? cognitionOverrides = null)
    {
        return new ActorTemplateData
        {
            TemplateId = Guid.NewGuid(),
            Category = category,
            BehaviorRef = "asset://behaviors/test-behavior",
            TickIntervalMs = 100,
            AutoSaveIntervalSeconds = 0,
            MaxInstancesPerNode = 100,
            CognitionTemplateId = cognitionTemplateId,
            CognitionOverrides = cognitionOverrides,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ActorServiceConfiguration CreateTestConfig()
    {
        return new ActorServiceConfiguration
        {
            PerceptionQueueSize = 100,
            DefaultTickIntervalMs = 100,
            DefaultAutoSaveIntervalSeconds = 0
        };
    }

    /// <summary>
    /// Creates a runner with configurable cognition builder behavior.
    /// </summary>
    private static (ActorRunner runner, Mock<ICognitionBuilder> builderMock, Mock<IDocumentExecutor> executorMock)
        CreateRunnerWithCognition(
            ActorTemplateData? template = null,
            AbmlDocument? document = null,
            ICognitionPipeline? pipelineToReturn = null,
            bool builderReturnsNull = false)
    {
        var messageBusMock = new Mock<IMessageBus>();
        messageBusMock.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var messageSubscriberMock = new Mock<IMessageSubscriber>();
        var meshClientMock = new Mock<IMeshInvocationClient>();

        var stateStoreMock = new Mock<IStateStore<ActorStateSnapshot>>();
        stateStoreMock.Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<ActorStateSnapshot>(),
                It.IsAny<StateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var testDoc = document ?? new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata { Id = "test-behavior" },
            Flows = new Dictionary<string, Flow>
            {
                ["main"] = new Flow { Name = "main", Actions = [] }
            }
        };

        var behaviorLoaderMock = new Mock<IBehaviorDocumentLoader>();
        behaviorLoaderMock.Setup(l => l.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(testDoc);

        var executorMock = new Mock<IDocumentExecutor>();
        executorMock.Setup(e => e.ExecuteAsync(
                It.IsAny<AbmlDocument>(), It.IsAny<string>(),
                It.IsAny<IVariableScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutionResult.Success());

        var expressionEvaluatorMock = new Mock<IExpressionEvaluator>();

        var cognitionBuilderMock = new Mock<ICognitionBuilder>();
        if (builderReturnsNull)
        {
            cognitionBuilderMock.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CognitionOverrides?>()))
                .Returns((ICognitionPipeline?)null);
        }
        else if (pipelineToReturn != null)
        {
            cognitionBuilderMock.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CognitionOverrides?>()))
                .Returns(pipelineToReturn);
        }

        var loggerMock = new Mock<ILogger<ActorRunner>>();

        var runner = new ActorRunner(
            $"actor-{Guid.NewGuid()}",
            template ?? CreateTestTemplate(),
            Guid.NewGuid(),
            CreateTestConfig(),
            messageBusMock.Object,
            messageSubscriberMock.Object,
            meshClientMock.Object,
            stateStoreMock.Object,
            behaviorLoaderMock.Object,
            new List<IVariableProviderFactory>(),
            executorMock.Object,
            expressionEvaluatorMock.Object,
            cognitionBuilderMock.Object,
            loggerMock.Object,
            null);

        return (runner, cognitionBuilderMock, executorMock);
    }

    private static async Task WaitForIterationsAsync(ActorRunner runner, int targetIterations, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (runner.LoopIterations < targetIterations)
        {
            if (DateTime.UtcNow - started > timeout)
            {
                throw new TimeoutException(
                    $"Timed out waiting for {targetIterations} iterations after {timeout.TotalSeconds}s. " +
                    $"Current iterations: {runner.LoopIterations}");
            }
            await Task.Delay(10);
        }
    }

    #region Template Resolution Order

    [Fact]
    public async Task TemplateResolution_ActorTemplateConfig_IsPrimary()
    {
        // Arrange: template has explicit cognition template ID
        var pipelineMock = new Mock<ICognitionPipeline>();
        pipelineMock.SetupGet(p => p.Stages).Returns(new List<ICognitionStage>());
        pipelineMock.SetupGet(p => p.TemplateId).Returns("custom-template");

        var template = CreateTestTemplate(cognitionTemplateId: "custom-template");
        var (runner, builderMock, _) = CreateRunnerWithCognition(
            template: template,
            pipelineToReturn: pipelineMock.Object);

        // Act: start runner and wait for first tick (which loads behavior and builds pipeline)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await runner.StartAsync(cts.Token);
        await WaitForIterationsAsync(runner, 1, TimeSpan.FromSeconds(5));
        await runner.StopAsync(cancellationToken: cts.Token);

        // Assert: builder was called with the template config ID
        builderMock.Verify(b => b.Build("custom-template", It.IsAny<CognitionOverrides?>()), Times.Once);
    }

    [Fact]
    public async Task TemplateResolution_AbmlMetadata_IsSecondary()
    {
        // Arrange: template has NO cognition template ID, but ABML metadata does
        var pipelineMock = new Mock<ICognitionPipeline>();
        pipelineMock.SetupGet(p => p.Stages).Returns(new List<ICognitionStage>());
        pipelineMock.SetupGet(p => p.TemplateId).Returns("abml-specified-template");

        var template = CreateTestTemplate(cognitionTemplateId: null);
        var document = new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata
            {
                Id = "test-behavior",
                CognitionTemplate = "abml-specified-template"
            },
            Flows = new Dictionary<string, Flow>
            {
                ["main"] = new Flow { Name = "main", Actions = [] }
            }
        };

        var (runner, builderMock, _) = CreateRunnerWithCognition(
            template: template,
            document: document,
            pipelineToReturn: pipelineMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await runner.StartAsync(cts.Token);
        await WaitForIterationsAsync(runner, 1, TimeSpan.FromSeconds(5));
        await runner.StopAsync(cancellationToken: cts.Token);

        // Assert: builder was called with the ABML metadata template ID
        builderMock.Verify(b => b.Build("abml-specified-template", It.IsAny<CognitionOverrides?>()), Times.Once);
    }

    [Fact]
    public async Task TemplateResolution_CategoryDefault_IsFallback()
    {
        // Arrange: no template config, no ABML metadata → falls back to category default
        var pipelineMock = new Mock<ICognitionPipeline>();
        pipelineMock.SetupGet(p => p.Stages).Returns(new List<ICognitionStage>());
        pipelineMock.SetupGet(p => p.TemplateId).Returns("humanoid-cognition-base");

        var template = CreateTestTemplate(category: "npc-brain", cognitionTemplateId: null);
        var (runner, builderMock, _) = CreateRunnerWithCognition(
            template: template,
            pipelineToReturn: pipelineMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await runner.StartAsync(cts.Token);
        await WaitForIterationsAsync(runner, 1, TimeSpan.FromSeconds(5));
        await runner.StopAsync(cancellationToken: cts.Token);

        // Assert: builder was called with the category default (npc-brain → humanoid-cognition-base)
        builderMock.Verify(b => b.Build("humanoid-cognition-base", It.IsAny<CognitionOverrides?>()), Times.Once);
    }

    [Fact]
    public async Task TemplateResolution_NoCategoryDefault_NoPipeline()
    {
        // Arrange: category "scheduled-task" has no default mapping
        var template = CreateTestTemplate(category: "scheduled-task", cognitionTemplateId: null);
        var (runner, builderMock, _) = CreateRunnerWithCognition(template: template);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await runner.StartAsync(cts.Token);
        await WaitForIterationsAsync(runner, 1, TimeSpan.FromSeconds(5));
        await runner.StopAsync(cancellationToken: cts.Token);

        // Assert: builder was never called (no template resolved)
        builderMock.Verify(b => b.Build(It.IsAny<string>(), It.IsAny<CognitionOverrides?>()), Times.Never);
    }

    #endregion

    #region Override Composition

    [Fact]
    public async Task OverrideComposition_TemplateAndInstanceOverrides_AreMerged()
    {
        // Arrange: template has overrides, instance state has overrides
        var templateOverride = new ParameterOverride
        {
            Stage = "filter",
            HandlerId = "attention_filter",
            Parameters = new Dictionary<string, object> { ["attention_budget"] = 200f }
        };
        var templateOverrides = new CognitionOverrides
        {
            Overrides = new List<ICognitionOverride> { templateOverride }
        };

        var instanceOverride = new ParameterOverride
        {
            Stage = "filter",
            HandlerId = "attention_filter",
            Parameters = new Dictionary<string, object> { ["max_perceptions"] = 20 }
        };
        var instanceOverrides = new CognitionOverrides
        {
            Overrides = new List<ICognitionOverride> { instanceOverride }
        };

        var pipelineMock = new Mock<ICognitionPipeline>();
        pipelineMock.SetupGet(p => p.Stages).Returns(new List<ICognitionStage>());
        pipelineMock.SetupGet(p => p.TemplateId).Returns("humanoid-cognition-base");

        var template = CreateTestTemplate(
            cognitionTemplateId: "humanoid-cognition-base",
            cognitionOverrides: templateOverrides);

        // Create initial state with instance overrides
        var initialState = new ActorStateSnapshot
        {
            ActorId = "test-actor",
            TemplateId = template.TemplateId,
            Category = "npc-brain",
            CognitionOverrides = instanceOverrides,
            Status = ActorStatus.Pending,
            StartedAt = DateTimeOffset.UtcNow
        };

        CognitionOverrides? capturedOverrides = null;
        var cognitionBuilderMock = new Mock<ICognitionBuilder>();
        cognitionBuilderMock.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CognitionOverrides?>()))
            .Callback<string, CognitionOverrides?>((_, overrides) => capturedOverrides = overrides)
            .Returns(pipelineMock.Object);

        var messageBusMock = new Mock<IMessageBus>();
        messageBusMock.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var stateStoreMock = new Mock<IStateStore<ActorStateSnapshot>>();
        stateStoreMock.Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<ActorStateSnapshot>(),
                It.IsAny<StateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var document = new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata { Id = "test" },
            Flows = new Dictionary<string, Flow>
            {
                ["main"] = new Flow { Name = "main", Actions = [] }
            }
        };
        var behaviorLoaderMock = new Mock<IBehaviorDocumentLoader>();
        behaviorLoaderMock.Setup(l => l.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var executorMock = new Mock<IDocumentExecutor>();
        executorMock.Setup(e => e.ExecuteAsync(
                It.IsAny<AbmlDocument>(), It.IsAny<string>(),
                It.IsAny<IVariableScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutionResult.Success());

        await using var runner = new ActorRunner(
            "test-actor",
            template,
            Guid.NewGuid(),
            CreateTestConfig(),
            messageBusMock.Object,
            new Mock<IMessageSubscriber>().Object,
            new Mock<IMeshInvocationClient>().Object,
            stateStoreMock.Object,
            behaviorLoaderMock.Object,
            new List<IVariableProviderFactory>(),
            executorMock.Object,
            new Mock<IExpressionEvaluator>().Object,
            cognitionBuilderMock.Object,
            new Mock<ILogger<ActorRunner>>().Object,
            initialState);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await runner.StartAsync(cts.Token);
        await WaitForIterationsAsync(runner, 1, TimeSpan.FromSeconds(5));
        await runner.StopAsync(cancellationToken: cts.Token);

        // Assert: overrides were merged (2 overrides: template + instance)
        Assert.NotNull(capturedOverrides);
        Assert.Equal(2, capturedOverrides.Overrides.Count);
    }

    #endregion

    #region Flow Resolution

    [Fact]
    public async Task FlowResolution_OnTick_PreferredOverMain()
    {
        // Arrange: behavior has both on_tick and main flows
        var document = new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata { Id = "test-behavior" },
            Flows = new Dictionary<string, Flow>
            {
                ["on_tick"] = new Flow { Name = "on_tick", Actions = [] },
                ["main"] = new Flow { Name = "main", Actions = [] }
            }
        };

        var template = CreateTestTemplate(category: "scheduled-task", cognitionTemplateId: null);
        var (runner, _, executorMock) = CreateRunnerWithCognition(
            template: template,
            document: document);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await runner.StartAsync(cts.Token);
        await WaitForIterationsAsync(runner, 1, TimeSpan.FromSeconds(5));
        await runner.StopAsync(cancellationToken: cts.Token);

        // Assert: executor was called with "on_tick" flow, not "main"
        executorMock.Verify(e => e.ExecuteAsync(
            It.IsAny<AbmlDocument>(),
            "on_tick",
            It.IsAny<IVariableScope>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task FlowResolution_MainFallback_WhenNoOnTick()
    {
        // Arrange: behavior only has "main" flow
        var document = new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata { Id = "test-behavior" },
            Flows = new Dictionary<string, Flow>
            {
                ["main"] = new Flow { Name = "main", Actions = [] }
            }
        };

        var template = CreateTestTemplate(category: "scheduled-task", cognitionTemplateId: null);
        var (runner, _, executorMock) = CreateRunnerWithCognition(
            template: template,
            document: document);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await runner.StartAsync(cts.Token);
        await WaitForIterationsAsync(runner, 1, TimeSpan.FromSeconds(5));
        await runner.StopAsync(cancellationToken: cts.Token);

        // Assert: executor was called with "main" flow
        executorMock.Verify(e => e.ExecuteAsync(
            It.IsAny<AbmlDocument>(),
            "main",
            It.IsAny<IVariableScope>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    #endregion

    #region Category Defaults

    [Theory]
    [InlineData("npc-brain", "humanoid-cognition-base")]
    [InlineData("event-combat", "creature-cognition-base")]
    [InlineData("event-regional", "creature-cognition-base")]
    [InlineData("world-admin", "object-cognition-base")]
    public void CognitionDefaults_ReturnsExpectedTemplate(string category, string expectedTemplateId)
    {
        var result = CognitionDefaults.GetDefaultTemplateId(category);
        Assert.Equal(expectedTemplateId, result);
    }

    [Theory]
    [InlineData("scheduled-task")]
    [InlineData("unknown-category")]
    [InlineData("")]
    public void CognitionDefaults_ReturnsNull_ForUnknownCategory(string category)
    {
        var result = CognitionDefaults.GetDefaultTemplateId(category);
        Assert.Null(result);
    }

    #endregion
}
