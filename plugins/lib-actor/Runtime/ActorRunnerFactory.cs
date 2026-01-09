using BeyondImmersion.BannouService.Actor.Caching;
using BeyondImmersion.BannouService.Actor.Execution;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Factory for creating ActorRunner instances with proper DI.
/// </summary>
public class ActorRunnerFactory : IActorRunnerFactory
{
    private readonly IMessageBus _messageBus;
    private readonly IMessageSubscriber _messageSubscriber;
    private readonly IMeshInvocationClient _meshClient;
    private readonly ActorServiceConfiguration _config;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IBehaviorDocumentCache _behaviorCache;
    private readonly IPersonalityCache _personalityCache;
    private readonly IDocumentExecutorFactory _executorFactory;

    /// <summary>
    /// Creates a new actor runner factory.
    /// </summary>
    /// <param name="messageBus">Message bus for publishing events.</param>
    /// <param name="messageSubscriber">Message subscriber for dynamic subscriptions.</param>
    /// <param name="meshClient">Mesh client for routing state updates to game servers.</param>
    /// <param name="config">Service configuration.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="stateStoreFactory">State store factory for actor persistence.</param>
    /// <param name="behaviorCache">Behavior document cache for loading ABML.</param>
    /// <param name="personalityCache">Personality cache for character traits.</param>
    /// <param name="executorFactory">Document executor factory for behavior execution.</param>
    public ActorRunnerFactory(
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber,
        IMeshInvocationClient meshClient,
        ActorServiceConfiguration config,
        ILoggerFactory loggerFactory,
        IStateStoreFactory stateStoreFactory,
        IBehaviorDocumentCache behaviorCache,
        IPersonalityCache personalityCache,
        IDocumentExecutorFactory executorFactory)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _messageSubscriber = messageSubscriber ?? throw new ArgumentNullException(nameof(messageSubscriber));
        _meshClient = meshClient ?? throw new ArgumentNullException(nameof(meshClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _behaviorCache = behaviorCache ?? throw new ArgumentNullException(nameof(behaviorCache));
        _personalityCache = personalityCache ?? throw new ArgumentNullException(nameof(personalityCache));
        _executorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
    }

    /// <inheritdoc/>
    public IActorRunner Create(
        string actorId,
        ActorTemplateData template,
        Guid? characterId = null,
        object? configurationOverrides = null,
        object? initialState = null)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(template);

        // Apply configuration overrides if provided
        var effectiveTemplate = template;
        if (configurationOverrides != null)
        {
            effectiveTemplate = ApplyConfigurationOverrides(template, configurationOverrides);
        }

        var logger = _loggerFactory.CreateLogger<ActorRunner>();

        // Get the actor-state store for this actor
        var stateStore = _stateStoreFactory.GetStore<ActorStateSnapshot>(_config.ActorStateStoreName);

        // Create a document executor for this actor
        var executor = _executorFactory.Create();

        return new ActorRunner(
            actorId,
            effectiveTemplate,
            characterId,
            _config,
            _messageBus,
            _messageSubscriber,
            _meshClient,
            stateStore,
            _behaviorCache,
            _personalityCache,
            executor,
            logger,
            initialState);
    }

    /// <summary>
    /// Applies configuration overrides to a template.
    /// </summary>
    /// <remarks>
    /// Configuration overrides REPLACE the template configuration entirely (no merge).
    /// This is intentional:
    /// - Configuration is generic (object?) - deep merging arbitrary types is error-prone
    /// - Explicit > implicit: callers who need partial overrides should merge at the call site
    /// - Simple semantics: "override = replace" is unambiguous
    ///
    /// If partial overrides are needed, the caller should:
    /// 1. Get the template configuration
    /// 2. Merge their changes into a new configuration object
    /// 3. Pass the merged configuration as the override
    /// </remarks>
    private static ActorTemplateData ApplyConfigurationOverrides(ActorTemplateData template, object overrides)
    {
        // Configuration overrides replace the template configuration entirely (no merge).
        // Callers requiring partial overrides should merge before calling.
        return new ActorTemplateData
        {
            TemplateId = template.TemplateId,
            Category = template.Category,
            BehaviorRef = template.BehaviorRef,
            Configuration = overrides ?? template.Configuration,
            AutoSpawn = template.AutoSpawn,
            TickIntervalMs = template.TickIntervalMs,
            AutoSaveIntervalSeconds = template.AutoSaveIntervalSeconds,
            MaxInstancesPerNode = template.MaxInstancesPerNode,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }
}
