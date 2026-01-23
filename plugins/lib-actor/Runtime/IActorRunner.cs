namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Interface for an actor runtime instance that executes behavior loops.
/// Each actor instance has one runner that manages its lifecycle.
/// </summary>
public interface IActorRunner : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique identifier for this actor instance.
    /// </summary>
    string ActorId { get; }

    /// <summary>
    /// Gets the template ID this actor was spawned from.
    /// </summary>
    Guid TemplateId { get; }

    /// <summary>
    /// Gets the category of this actor (e.g., "npc-brain", "world-admin").
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Gets the optional character ID for NPC brain actors.
    /// </summary>
    Guid? CharacterId { get; }

    /// <summary>
    /// Gets the current status of this actor.
    /// </summary>
    ActorStatus Status { get; }

    /// <summary>
    /// Gets the time this actor was started.
    /// </summary>
    DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Gets the time of the last heartbeat/tick.
    /// </summary>
    DateTimeOffset? LastHeartbeat { get; }

    /// <summary>
    /// Gets the total number of behavior loop iterations executed.
    /// </summary>
    long LoopIterations { get; }

    /// <summary>
    /// Gets the current depth of the perception queue.
    /// </summary>
    int PerceptionQueueDepth { get; }

    /// <summary>
    /// Starts the actor's behavior loop.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the loop.</param>
    /// <returns>A task that completes when the actor starts.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the actor's behavior loop.
    /// </summary>
    /// <param name="graceful">If true, allows the current tick to complete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the actor stops.</returns>
    Task StopAsync(bool graceful = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Injects a perception into the actor's perception queue.
    /// </summary>
    /// <param name="perception">The perception data to inject.</param>
    /// <returns>True if the perception was queued, false if the queue is full (oldest will be dropped).</returns>
    bool InjectPerception(PerceptionData perception);

    /// <summary>
    /// Gets the current state snapshot of this actor.
    /// </summary>
    /// <returns>A snapshot of the actor's current state.</returns>
    ActorStateSnapshot GetStateSnapshot();

    /// <summary>
    /// Gets the current encounter ID if this actor is managing one (Event Brain).
    /// </summary>
    Guid? CurrentEncounterId { get; }

    /// <summary>
    /// Starts a new encounter managed by this actor (Event Brain).
    /// </summary>
    /// <param name="encounterId">Unique identifier for the encounter.</param>
    /// <param name="encounterType">Type of encounter (e.g., "combat", "conversation").</param>
    /// <param name="participants">Character IDs participating in the encounter.</param>
    /// <param name="initialData">Optional initial encounter data.</param>
    /// <returns>True if the encounter was started, false if one is already active.</returns>
    bool StartEncounter(Guid encounterId, string encounterType, IReadOnlyList<Guid> participants, Dictionary<string, object?>? initialData = null);

    /// <summary>
    /// Updates the current encounter's phase.
    /// </summary>
    /// <param name="phase">The new phase name.</param>
    /// <returns>True if the phase was updated, false if no encounter is active.</returns>
    bool SetEncounterPhase(string phase);

    /// <summary>
    /// Ends the current encounter managed by this actor.
    /// </summary>
    /// <returns>True if an encounter was ended, false if none was active.</returns>
    bool EndEncounter();
}
