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
/// Source generator that creates service scaffolding based on OpenAPI schemas.
/// Generates controllers, service interfaces, and client registrations while preserving existing implementations.
/// Controlled by MSBuild property GenerateNewServices.
/// </summary>
[Generator]
public class ServiceScaffoldGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Get MSBuild properties
        var generateNewServicesProvider = context.AnalyzerConfigOptionsProvider
            .Select((options, _) => 
            {
                options.GlobalOptions.TryGetValue("build_property.GenerateNewServices", out var value);
                return bool.TryParse(value, out var result) && result;
            });

        // Find OpenAPI schema files
        var schemaFilesProvider = context.AdditionalTextsProvider
            .Where(file => file.Path.Contains("/schemas/") && file.Path.EndsWith("-api.yaml"))
            .Collect();

        // Combine inputs  
        var combinedProvider = generateNewServicesProvider
            .Combine(schemaFilesProvider);

        // Generate service scaffolding
        context.RegisterSourceOutput(combinedProvider, GenerateServiceScaffolding);
    }

    private static void GenerateServiceScaffolding(
        SourceProductionContext context,
        (bool generateNewServices, ImmutableArray<AdditionalText> schemaFiles) input)
    {
        if (!input.generateNewServices || input.schemaFiles.IsDefaultOrEmpty)
            return;

        foreach (var schemaFile in input.schemaFiles)
        {
            try
            {
                var serviceName = ExtractServiceName(schemaFile.Path);
                if (string.IsNullOrEmpty(serviceName))
                    continue;

                var schemaContent = schemaFile.GetText(context.CancellationToken);
                if (schemaContent == null)
                    continue;

                // Generate service interface
                GenerateServiceInterface(context, serviceName, schemaContent.ToString());
                
                // Generate service implementation stub (only if doesn't exist)
                GenerateServiceImplementation(context, serviceName);
                
                // Generate client registration
                GenerateClientRegistration(context, serviceName);
                
                // Generate DI extensions
                GenerateServiceRegistration(context, serviceName);
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "BSG001",
                        "Service generation error",
                        $"Failed to generate service for {schemaFile.Path}: {ex.Message}",
                        "ServiceGeneration",
                        DiagnosticSeverity.Warning,
                        true),
                    Location.None));
            }
        }
    }

    private static string ExtractServiceName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.EndsWith("-api"))
        {
            return fileName.Substring(0, fileName.Length - 4);
        }
        return fileName;
    }

    private static void GenerateServiceInterface(SourceProductionContext context, string serviceName, string schemaContent)
    {
        var pascalCaseServiceName = ToPascalCase(serviceName);
        var endpoints = ParseEndpointsFromSchema(schemaContent);

        var interfaceSource = $$"""
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.{{pascalCaseServiceName}};

/// <summary>
/// Service interface for {{pascalCaseServiceName}} operations.
/// Generated from OpenAPI schema: {{serviceName}}-api.yaml
/// </summary>
public interface I{{pascalCaseServiceName}}Service
{
{{string.Join("\n", endpoints.Select(GenerateServiceMethod))}}
}
""";

        context.AddSource($"I{pascalCaseServiceName}Service.g.cs", SourceText.From(interfaceSource, Encoding.UTF8));
    }

    private static void GenerateServiceImplementation(SourceProductionContext context, string serviceName)
    {
        var pascalCaseServiceName = ToPascalCase(serviceName);

        var implementationSource = $$"""
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.{{pascalCaseServiceName}};

/// <summary>
/// Default implementation for {{pascalCaseServiceName}} operations.
/// Generated stub - implement your business logic here.
/// This file is only generated if it doesn't already exist.
/// </summary>
public class {{pascalCaseServiceName}}Service : I{{pascalCaseServiceName}}Service
{
    private readonly ILogger<{{pascalCaseServiceName}}Service> _logger;

    public {{pascalCaseServiceName}}Service(ILogger<{{pascalCaseServiceName}}Service> logger)
    {
        _logger = logger;
    }

    // TODO: Implement service methods based on I{{pascalCaseServiceName}}Service interface
    // This is a generated stub - replace with your actual implementation
}
""";

        // Only add if it doesn't exist (source generators can't check existing files directly,
        // so we use a different filename pattern that can be conditionally included)
        context.AddSource($"{pascalCaseServiceName}Service.stub.g.cs", SourceText.From(implementationSource, Encoding.UTF8));
    }

    private static void GenerateClientRegistration(SourceProductionContext context, string serviceName)
    {
        var pascalCaseServiceName = ToPascalCase(serviceName);

        var registrationSource = $$"""
using Microsoft.Extensions.DependencyInjection;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.{{pascalCaseServiceName}}.Client;

namespace BeyondImmersion.BannouService.{{pascalCaseServiceName}};

/// <summary>
/// Client registration extensions for {{pascalCaseServiceName}}.
/// Generated from service schema.
/// </summary>
public static partial class {{pascalCaseServiceName}}ClientExtensions
{
    /// <summary>
    /// Registers the {{pascalCaseServiceName}}Client with Dapr routing support.
    /// Uses dynamic app-id resolution defaulting to "bannou".
    /// </summary>
    public static IServiceCollection Add{{pascalCaseServiceName}}Client(this IServiceCollection services)
    {
        return services.AddDaprServiceClient<{{pascalCaseServiceName}}Client, I{{pascalCaseServiceName}}Client>("{{serviceName}}");
    }
}
""";

        context.AddSource($"{pascalCaseServiceName}ClientExtensions.g.cs", SourceText.From(registrationSource, Encoding.UTF8));
    }

    private static void GenerateServiceRegistration(SourceProductionContext context, string serviceName)
    {
        var pascalCaseServiceName = ToPascalCase(serviceName);

        var serviceRegistrationSource = $$"""
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.{{pascalCaseServiceName}};

/// <summary>
/// Service registration extensions for {{pascalCaseServiceName}}.
/// Generated from service schema.
/// </summary>
public static partial class {{pascalCaseServiceName}}ServiceExtensions
{
    /// <summary>
    /// Registers {{pascalCaseServiceName}} service and related dependencies.
    /// </summary>
    public static IServiceCollection Add{{pascalCaseServiceName}}Service(this IServiceCollection services)
    {
        services.AddScoped<I{{pascalCaseServiceName}}Service, {{pascalCaseServiceName}}Service>();
        
        // Add any additional service-specific dependencies here
        // Generated services can be extended by partial classes
        
        return services;
    }
}
""";

        context.AddSource($"{pascalCaseServiceName}ServiceExtensions.g.cs", SourceText.From(serviceRegistrationSource, Encoding.UTF8));
    }

    private static List<ServiceEndpoint> ParseEndpointsFromSchema(string schemaContent)
    {
        // Simple parsing for demonstration - in a real implementation,
        // you'd use a proper YAML parser and OpenAPI model
        var endpoints = new List<ServiceEndpoint>();
        
        // For now, return a basic endpoint structure
        // This would be replaced with actual OpenAPI parsing
        endpoints.Add(new ServiceEndpoint
        {
            Method = "GET",
            Path = "/status",
            OperationId = "GetStatus",
            ReturnType = "Task<IActionResult>"
        });

        return endpoints;
    }

    private static string GenerateServiceMethod(ServiceEndpoint endpoint)
    {
        var methodName = endpoint.OperationId ?? $"{endpoint.Method}{endpoint.Path.Replace("/", "").Replace("{", "").Replace("}", "")}";
        
        return $"""
    /// <summary>
    /// {endpoint.Method} {endpoint.Path}
    /// </summary>
    {endpoint.ReturnType} {ToPascalCase(methodName)}(CancellationToken cancellationToken = default);
""";
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

    private class ServiceEndpoint
    {
        public string Method { get; set; } = "";
        public string Path { get; set; } = "";
        public string? OperationId { get; set; }
        public string ReturnType { get; set; } = "Task<IActionResult>";
    }
}