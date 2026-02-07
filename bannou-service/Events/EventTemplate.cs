#nullable enable

namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Definition of a publishable event template for emit_event: ABML action.
/// Templates are registered by the plugin that owns the event type.
/// </summary>
/// <remarks>
/// <para>
/// This follows the same ownership pattern as compression callbacks and variable provider factories:
/// each L3/L4 plugin registers templates for events it owns. The handler (in lib-actor) looks up
/// templates by name and performs substitution using the existing TemplateSubstitutor.
/// </para>
/// <para>
/// Example registration in CharacterEncounterService.OnRunningAsync:
/// <code>
/// _eventTemplateRegistry.Register(new EventTemplate(
///     Name: "encounter_resolved",
///     Topic: "encounter.resolved",
///     EventType: typeof(EncounterResolvedEvent),
///     PayloadTemplate: @"{
///         ""encounterId"": ""{{encounterId}}"",
///         ""outcome"": ""{{outcome}}""
///     }",
///     Description: "Encounter completed with outcome"
/// ));
/// </code>
/// </para>
/// </remarks>
/// <param name="Name">Template name used in ABML (e.g., "encounter_resolved").</param>
/// <param name="Topic">Event topic to publish to (e.g., "encounter.resolved").</param>
/// <param name="EventType">Event type for schema validation at registration and runtime.</param>
/// <param name="PayloadTemplate">JSON payload template with {{variable}} placeholders.</param>
/// <param name="Description">Human-readable description of what this event represents.</param>
public sealed record EventTemplate(
    string Name,
    string Topic,
    Type EventType,
    string PayloadTemplate,
    string? Description = null
);
