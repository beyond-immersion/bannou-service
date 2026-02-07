#nullable enable

namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Registry of event templates for emit_event: ABML action.
/// Each plugin registers templates for events it owns during OnRunningAsync.
/// </summary>
/// <remarks>
/// <para>
/// This follows the same pattern as other plugin-registered infrastructure:
/// - Compression callbacks: Each L4 plugin registers callbacks for its resource types
/// - Variable providers: Each L4 plugin provides factories for its data types
/// - Event templates: Each plugin registers templates for events it owns
/// </para>
/// <para>
/// The registry validates templates at registration time:
/// - Template name uniqueness
/// - EventType property matching with template variables
/// - Basic JSON template validity
/// </para>
/// <para>
/// Thread-safety: All operations are thread-safe. Registration typically happens
/// during plugin startup, and lookups occur during behavior execution.
/// </para>
/// </remarks>
public interface IEventTemplateRegistry
{
    /// <summary>
    /// Registers an event template. Called by plugins at startup.
    /// Validates template structure and event type properties.
    /// </summary>
    /// <param name="template">The template to register.</param>
    /// <exception cref="ArgumentNullException">If template is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// If template already exists, or if validation fails (missing properties, invalid template).
    /// </exception>
    void Register(EventTemplate template);

    /// <summary>
    /// Gets a template by name. Returns null if not found.
    /// </summary>
    /// <param name="templateName">The template name to look up.</param>
    /// <returns>The template, or null if not registered.</returns>
    EventTemplate? Get(string templateName);

    /// <summary>
    /// Checks if a template exists.
    /// </summary>
    /// <param name="templateName">The template name to check.</param>
    /// <returns>True if the template is registered.</returns>
    bool Exists(string templateName);

    /// <summary>
    /// Gets all registered template names.
    /// </summary>
    /// <returns>Collection of registered template names.</returns>
    IReadOnlyCollection<string> GetTemplateNames();
}
