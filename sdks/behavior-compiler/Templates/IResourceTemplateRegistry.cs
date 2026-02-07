// =============================================================================
// Resource Template Registry Interface
// Registry for managing resource templates used in compile-time validation.
// =============================================================================

namespace BeyondImmersion.Bannou.BehaviorCompiler.Templates;

/// <summary>
/// Registry of resource templates for compile-time validation.
/// Templates are registered by plugins during startup.
/// </summary>
/// <remarks>
/// <para>
/// The registry maintains two lookup indexes:
/// <list type="bullet">
///   <item>By sourceType: matches ABML resource_templates metadata values</item>
///   <item>By namespace: matches the first path segment in expressions</item>
/// </list>
/// </para>
/// <para>
/// <b>Usage in SemanticAnalyzer</b>:
/// </para>
/// <code>
/// // Validate declared templates exist
/// if (!registry.HasTemplate(templateName))
/// {
///     warnings.Add($"Unknown resource template: {templateName}");
/// }
///
/// // Validate expression paths
/// var template = registry.GetByNamespace(firstSegment);
/// if (template != null)
/// {
///     var result = template.ValidatePath(remainingPath);
///     if (!result.IsValid)
///     {
///         errors.Add(result.ErrorMessage);
///     }
/// }
/// </code>
/// <para>
/// <b>Implementation</b>:
/// The concrete ResourceTemplateRegistry class lives in bannou-service
/// and is registered as a singleton in DI. Plugins inject it via
/// constructor injection and register their templates in OnRunningAsync.
/// </para>
/// </remarks>
public interface IResourceTemplateRegistry
{
    /// <summary>
    /// Gets a template by its sourceType (e.g., "character-personality").
    /// </summary>
    /// <param name="sourceType">The sourceType identifier from x-compression-callback.</param>
    /// <returns>The template, or null if not registered.</returns>
    IResourceTemplate? GetBySourceType(string sourceType);

    /// <summary>
    /// Gets a template by its namespace (e.g., "personality").
    /// </summary>
    /// <param name="namespace">The namespace used in ABML expressions.</param>
    /// <returns>The template, or null if not registered.</returns>
    IResourceTemplate? GetByNamespace(string @namespace);

    /// <summary>
    /// Gets all registered templates.
    /// </summary>
    /// <returns>All registered templates.</returns>
    IEnumerable<IResourceTemplate> GetAllTemplates();

    /// <summary>
    /// Registers a template.
    /// </summary>
    /// <param name="template">The template to register.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if a template with the same sourceType or namespace is already registered.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown if template is null.
    /// </exception>
    void Register(IResourceTemplate template);

    /// <summary>
    /// Checks if a sourceType has a registered template.
    /// </summary>
    /// <param name="sourceType">The sourceType to check.</param>
    /// <returns>True if a template is registered for this sourceType.</returns>
    bool HasTemplate(string sourceType);
}
