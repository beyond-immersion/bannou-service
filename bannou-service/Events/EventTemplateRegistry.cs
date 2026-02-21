#nullable enable

using BeyondImmersion.BannouService.Utilities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;

namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Thread-safe singleton registry for event templates.
/// Validates templates at registration time and provides fast lookup by name.
/// </summary>
public sealed class EventTemplateRegistry : IEventTemplateRegistry
{
    private readonly ConcurrentDictionary<string, EventTemplate> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<EventTemplateRegistry> _logger;

    /// <summary>
    /// Creates a new EventTemplateRegistry.
    /// </summary>
    /// <param name="logger">Logger for registration and lookup events.</param>
    public EventTemplateRegistry(ILogger<EventTemplateRegistry> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Register(EventTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        // Validate required fields
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            throw new InvalidOperationException("Event template name cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(template.Topic))
        {
            throw new InvalidOperationException($"Event template '{template.Name}' must have a topic");
        }

        if (template.EventType == null)
        {
            throw new InvalidOperationException($"Event template '{template.Name}' must have an EventType");
        }

        if (string.IsNullOrWhiteSpace(template.PayloadTemplate))
        {
            throw new InvalidOperationException($"Event template '{template.Name}' must have a PayloadTemplate");
        }

        // Validate template variables match EventType properties
        ValidateTemplateAgainstEventType(template);

        // Attempt to register (thread-safe)
        if (!_templates.TryAdd(template.Name, template))
        {
            throw new InvalidOperationException(
                $"Event template '{template.Name}' is already registered. " +
                $"Each template name must be unique across all plugins.");
        }

        _logger.LogInformation(
            "Registered event template: {TemplateName} -> {Topic} ({EventType})",
            template.Name, template.Topic, template.EventType.Name);
    }

    /// <inheritdoc/>
    public EventTemplate? Get(string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return null;
        }

        if (_templates.TryGetValue(templateName, out var template))
        {
            return template;
        }

        _logger.LogDebug("Event template not found: {TemplateName}", templateName);
        return null;
    }

    /// <inheritdoc/>
    public bool Exists(string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return false;
        }

        return _templates.ContainsKey(templateName);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetTemplateNames()
    {
        return _templates.Keys.ToList();
    }

    /// <summary>
    /// Validates that all template variables have corresponding properties in the EventType.
    /// </summary>
    private void ValidateTemplateAgainstEventType(EventTemplate template)
    {
        var variables = TemplateSubstitutor.ExtractVariables(template.PayloadTemplate);
        if (variables.Count == 0)
        {
            _logger.LogWarning(
                "Event template '{TemplateName}' has no variables - payload is static",
                template.Name);
            return;
        }

        var eventProperties = template.EventType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertyNames = eventProperties
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingProperties = new List<string>();

        foreach (var variable in variables)
        {
            // For simple variables (no dot path), check direct property match
            // For dot paths (e.g., "party.id"), only validate the root segment
            var rootVariable = variable.Contains('.')
                ? variable.Substring(0, variable.IndexOf('.'))
                : variable.Contains('[')
                    ? variable.Substring(0, variable.IndexOf('['))
                    : variable;

            if (!propertyNames.Contains(rootVariable))
            {
                missingProperties.Add(variable);
            }
        }

        if (missingProperties.Count > 0)
        {
            throw new InvalidOperationException(
                $"Event template '{template.Name}' references variables that don't exist on {template.EventType.Name}: " +
                $"{string.Join(", ", missingProperties.Select(v => $"{{{{v}}}}"))}. " +
                $"Available properties: {string.Join(", ", propertyNames)}");
        }

        _logger.LogDebug(
            "Event template '{TemplateName}' validated: {VariableCount} variables match {EventType}",
            template.Name, variables.Count, template.EventType.Name);
    }
}
