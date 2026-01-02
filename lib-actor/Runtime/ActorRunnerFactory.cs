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
    private readonly ActorServiceConfiguration _config;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Creates a new actor runner factory.
    /// </summary>
    /// <param name="messageBus">Message bus for publishing events.</param>
    /// <param name="config">Service configuration.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public ActorRunnerFactory(
        IMessageBus messageBus,
        ActorServiceConfiguration config,
        ILoggerFactory loggerFactory)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
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

        return new ActorRunner(
            actorId,
            effectiveTemplate,
            characterId,
            _config,
            _messageBus,
            logger,
            initialState);
    }

    /// <summary>
    /// Applies configuration overrides to a template.
    /// </summary>
    private static ActorTemplateData ApplyConfigurationOverrides(ActorTemplateData template, object overrides)
    {
        // Create a copy with overrides applied
        // For now, just return the template - override logic would parse the object
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
