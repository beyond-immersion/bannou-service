using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using System.Linq;
using System.IO;
using System;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.SourceGeneration;

/// <summary>
/// Source generator that creates event models and pub/sub handlers from OpenAPI event schemas.
/// Generates event classes, publisher services, and subscriber handlers.
/// </summary>
[Generator]
public class EventModelGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find event schema files
        var eventSchemaFilesProvider = context.AdditionalTextsProvider
            .Where(file => file.Path.Contains("/schemas/") && file.Path.EndsWith("-events.yaml"))
            .Collect();

        // Generate event models and handlers
        context.RegisterSourceOutput(eventSchemaFilesProvider, GenerateEventModelsAndHandlers);
    }

    private static void GenerateEventModelsAndHandlers(
        SourceProductionContext context,
        ImmutableArray<AdditionalText> eventSchemaFiles)
    {
        if (eventSchemaFiles.IsDefaultOrEmpty)
            return;

        foreach (var schemaFile in eventSchemaFiles)
        {
            try
            {
                var serviceName = ExtractServiceName(schemaFile.Path);
                if (string.IsNullOrEmpty(serviceName))
                    continue;

                var schemaContent = schemaFile.GetText(context.CancellationToken);
                if (schemaContent == null)
                    continue;

                // Generate event models (data classes)
                GenerateEventModels(context, serviceName, schemaContent.ToString());
                
                // Generate event publisher service
                GenerateEventPublisher(context, serviceName);
                
                // Generate event subscriber handlers
                GenerateEventSubscriber(context, serviceName);
                
                // Generate DI extensions for events
                GenerateEventRegistrations(context, serviceName);
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "BEG001",
                        "Event generation error",
                        $"Failed to generate events for {schemaFile.Path}: {ex.Message}",
                        "EventGeneration",
                        DiagnosticSeverity.Warning,
                        true),
                    Location.None));
            }
        }
    }

    private static string ExtractServiceName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.EndsWith("-events"))
        {
            return fileName.Substring(0, fileName.Length - 7);
        }
        return fileName;
    }

    private static void GenerateEventModels(SourceProductionContext context, string serviceName, string schemaContent)
    {
        var pascalCaseServiceName = ToPascalCase(serviceName);
        var events = ParseEventsFromSchema(schemaContent);

        var eventModelsSource = $$"""
using System;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.{{pascalCaseServiceName}}.Events;

/// <summary>
/// Event models for {{pascalCaseServiceName}} service.
/// Generated from OpenAPI schema: {{serviceName}}-events.yaml
/// </summary>

/// <summary>
/// Base class for all {{pascalCaseServiceName}} events.
/// </summary>
public abstract class {{pascalCaseServiceName}}EventBase
{
    /// <summary>
    /// Unique identifier for this event.
    /// </summary>
    [Required]
    [JsonProperty("eventId")]
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// When the event occurred.
    /// </summary>
    [Required]
    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

{{string.Join("\n\n", events.Select(e => GenerateEventClass(pascalCaseServiceName, e)))}}
""";

        context.AddSource($"{pascalCaseServiceName}EventModels.g.cs", SourceText.From(eventModelsSource, Encoding.UTF8));
    }

    private static void GenerateEventPublisher(SourceProductionContext context, string serviceName)
    {
        var pascalCaseServiceName = ToPascalCase(serviceName);

        var publisherSource = $$"""
using Dapr.Client;
using Microsoft.Extensions.Logging;
using BeyondImmersion.BannouService.{{pascalCaseServiceName}}.Events;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.{{pascalCaseServiceName}}.Events;

/// <summary>
/// Publisher service for {{pascalCaseServiceName}} events via Dapr pub/sub.
/// </summary>
public interface I{{pascalCaseServiceName}}EventPublisher
{
    /// <summary>
    /// Publishes an event to the bannou-{{serviceName}}-events channel.
    /// </summary>
    Task PublishEventAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : {{pascalCaseServiceName}}EventBase;
}

/// <summary>
/// Default implementation of {{pascalCaseServiceName}} event publisher.
/// </summary>
public class {{pascalCaseServiceName}}EventPublisher : I{{pascalCaseServiceName}}EventPublisher
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<{{pascalCaseServiceName}}EventPublisher> _logger;
    private const string PUB_SUB_NAME = "bannou-pubsub";
    private const string TOPIC_NAME = "bannou-{{serviceName}}-events";

    public {{pascalCaseServiceName}}EventPublisher(
        DaprClient daprClient,
        ILogger<{{pascalCaseServiceName}}EventPublisher> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public async Task PublishEventAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : {{pascalCaseServiceName}}EventBase
    {
        try
        {
            _logger.LogDebug("Publishing {{pascalCaseServiceName}} event {EventType} with ID {EventId}",
                typeof(TEvent).Name, eventData.EventId);

            await _daprClient.PublishEventAsync(PUB_SUB_NAME, TOPIC_NAME, eventData, cancellationToken);
            
            _logger.LogInformation("Successfully published {{pascalCaseServiceName}} event {EventType} with ID {EventId}",
                typeof(TEvent).Name, eventData.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish {{pascalCaseServiceName}} event {EventType} with ID {EventId}",
                typeof(TEvent).Name, eventData.EventId);
            throw;
        }
    }
}
""";

        context.AddSource($"{pascalCaseServiceName}EventPublisher.g.cs", SourceText.From(publisherSource, Encoding.UTF8));
    }

    private static void GenerateEventSubscriber(SourceProductionContext context, string serviceName)
    {
        var pascalCaseServiceName = ToPascalCase(serviceName);

        var subscriberSource = $$"""
using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BeyondImmersion.BannouService.{{pascalCaseServiceName}}.Events;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.{{pascalCaseServiceName}}.Events;

/// <summary>
/// Subscriber for {{pascalCaseServiceName}} events via Dapr pub/sub.
/// Override methods in partial class to handle specific events.
/// </summary>
[ApiController]
[Route("api/events/{{serviceName}}")]
public partial class {{pascalCaseServiceName}}EventSubscriber : ControllerBase
{
    private readonly ILogger<{{pascalCaseServiceName}}EventSubscriber> _logger;

    public {{pascalCaseServiceName}}EventSubscriber(ILogger<{{pascalCaseServiceName}}EventSubscriber> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles all {{pascalCaseServiceName}} events from the bannou-{{serviceName}}-events topic.
    /// </summary>
    [Topic("bannou-pubsub", "bannou-{{serviceName}}-events")]
    [HttpPost("handle")]
    public async Task<IActionResult> HandleEventAsync([FromBody] {{pascalCaseServiceName}}EventBase eventData)
    {
        try
        {
            _logger.LogDebug("Received {{pascalCaseServiceName}} event {EventType} with ID {EventId}",
                eventData.GetType().Name, eventData.EventId);

            // Route to specific handler based on event type
            var handled = await RouteEventAsync(eventData);
            
            if (handled)
            {
                _logger.LogInformation("Successfully handled {{pascalCaseServiceName}} event {EventType} with ID {EventId}",
                    eventData.GetType().Name, eventData.EventId);
                return Ok();
            }
            else
            {
                _logger.LogWarning("No handler found for {{pascalCaseServiceName}} event {EventType} with ID {EventId}",
                    eventData.GetType().Name, eventData.EventId);
                return BadRequest("No handler found for event type");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle {{pascalCaseServiceName}} event {EventType} with ID {EventId}",
                eventData.GetType().Name, eventData.EventId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Routes events to specific handlers. Override in partial class to add custom routing.
    /// </summary>
    protected virtual async Task<bool> RouteEventAsync({{pascalCaseServiceName}}EventBase eventData)
    {
        // Default implementation - override in partial class for custom event handling
        _logger.LogDebug("Default event routing for {EventType}", eventData.GetType().Name);
        return await Task.FromResult(true);
    }
}
""";

        context.AddSource($"{pascalCaseServiceName}EventSubscriber.g.cs", SourceText.From(subscriberSource, Encoding.UTF8));
    }

    private static void GenerateEventRegistrations(SourceProductionContext context, string serviceName)
    {
        var pascalCaseServiceName = ToPascalCase(serviceName);

        var registrationSource = $$"""
using Microsoft.Extensions.DependencyInjection;
using BeyondImmersion.BannouService.{{pascalCaseServiceName}}.Events;

namespace BeyondImmersion.BannouService.{{pascalCaseServiceName}}.Events;

/// <summary>
/// Event registration extensions for {{pascalCaseServiceName}}.
/// </summary>
public static partial class {{pascalCaseServiceName}}EventExtensions
{
    /// <summary>
    /// Registers {{pascalCaseServiceName}} event publisher and subscriber.
    /// </summary>
    public static IServiceCollection Add{{pascalCaseServiceName}}Events(this IServiceCollection services)
    {
        services.AddScoped<I{{pascalCaseServiceName}}EventPublisher, {{pascalCaseServiceName}}EventPublisher>();
        services.AddScoped<{{pascalCaseServiceName}}EventSubscriber>();
        
        return services;
    }
}
""";

        context.AddSource($"{pascalCaseServiceName}EventExtensions.g.cs", SourceText.From(registrationSource, Encoding.UTF8));
    }

    private static List<EventModel> ParseEventsFromSchema(string schemaContent)
    {
        // Simple parsing for demonstration - in real implementation,
        // you'd use proper YAML parser and OpenAPI model
        var events = new List<EventModel>();
        
        // Parse basic event structures (simplified)
        if (schemaContent.Contains("AccountCreatedEvent"))
        {
            events.Add(new EventModel
            {
                Name = "AccountCreatedEvent",
                Properties = new List<EventProperty>
                {
                    new() { Name = "AccountId", Type = "string", Required = true },
                    new() { Name = "Username", Type = "string", Required = true },
                    new() { Name = "Email", Type = "string", Required = false }
                }
            });
        }

        if (schemaContent.Contains("ServiceMappingEvent"))
        {
            events.Add(new EventModel
            {
                Name = "ServiceMappingEvent", 
                Properties = new List<EventProperty>
                {
                    new() { Name = "ServiceName", Type = "string", Required = true },
                    new() { Name = "AppId", Type = "string", Required = true },
                    new() { Name = "Action", Type = "string", Required = true }
                }
            });
        }

        return events;
    }

    private static string GenerateEventClass(string serviceNamePascal, EventModel eventModel)
    {
        var properties = eventModel.Properties.Select(p => GenerateEventProperty(p));
        
        return $$"""
/// <summary>
/// {{eventModel.Name}} event for {{serviceNamePascal}} service.
/// </summary>
public class {{eventModel.Name}} : {{serviceNamePascal}}EventBase
{
{{string.Join("\n\n", properties)}}
}
""";
    }

    private static string GenerateEventProperty(EventProperty property)
    {
        var attributes = new List<string>();
        
        if (property.Required)
            attributes.Add("[Required]");
        
        attributes.Add($"[JsonProperty(\"{ToCamelCase(property.Name)}\")]");

        var csharpType = MapTypeToCSharp(property.Type);
        if (!property.Required && csharpType != "string")
            csharpType += "?";

        return $$"""
    {{string.Join("\n    ", attributes)}}
    public {{csharpType}} {{property.Name}} { get; set; }{{(property.Required ? "" : " = null;")}}
""";
    }

    private static string MapTypeToCSharp(string openApiType)
    {
        return openApiType switch
        {
            "string" => "string",
            "integer" => "int",
            "number" => "decimal",
            "boolean" => "bool",
            "array" => "List<string>", // Simplified
            "object" => "object",
            _ => "string"
        };
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var words = input.Split('-', '_', ' ')
            .Where(word => !string.IsNullOrEmpty(word))
            .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant());

        return string.Join("", words);
    }

    private static string ToCamelCase(string input)
    {
        var pascalCase = ToPascalCase(input);
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;

        return char.ToLowerInvariant(pascalCase[0]) + pascalCase.Substring(1);
    }

    private class EventModel
    {
        public string Name { get; set; } = "";
        public List<EventProperty> Properties { get; set; } = new();
    }

    private class EventProperty
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool Required { get; set; }
    }
}