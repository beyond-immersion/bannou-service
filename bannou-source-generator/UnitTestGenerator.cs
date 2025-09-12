using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace BeyondImmersion.BannouService.SourceGeneration;

/// <summary>
/// Source generator that creates comprehensive unit test projects for schema-driven services.
/// Generates test classes, fixtures, and configuration based on OpenAPI schemas and service implementations.
/// </summary>
[Generator]
public class UnitTestGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Get MSBuild properties
        var generateTestsProvider = context.AnalyzerConfigOptionsProvider
            .Select((options, _) =>
            {
                options.GlobalOptions.TryGetValue("build_property.GenerateUnitTests", out var value);
                return bool.TryParse(value, out var result) && result;
            });

        // Find service library projects
        var serviceLibsProvider = context.AdditionalTextsProvider
            .Where(file => file.Path.Contains("/lib-") && file.Path.EndsWith(".csproj"))
            .Collect();

        // Find OpenAPI schema files
        var schemaFilesProvider = context.AdditionalTextsProvider
            .Where(file => file.Path.Contains("/schemas/") && file.Path.EndsWith("-api.yaml"))
            .Collect();

        // Combine inputs
        var combinedProvider = generateTestsProvider
            .Combine(serviceLibsProvider)
            .Combine(schemaFilesProvider);

        // Generate unit test projects
        context.RegisterSourceOutput(combinedProvider, GenerateUnitTestProjects);
    }

    private static void GenerateUnitTestProjects(
        SourceProductionContext context,
        ((bool generateTests, ImmutableArray<AdditionalText> serviceLibs), ImmutableArray<AdditionalText> schemaFiles) input)
    {
        if (!input.Item1.generateTests || input.Item1.serviceLibs.IsDefaultOrEmpty)
            return;

        foreach (var serviceLib in input.Item1.serviceLibs)
        {
            var serviceName = ExtractServiceName(serviceLib.Path);
            if (string.IsNullOrEmpty(serviceName) || serviceName == "testing-core")
                continue;

            // Find corresponding schema file
            var schemaFile = input.schemaFiles.FirstOrDefault(schema =>
                schema.Path.Contains($"/{serviceName}-api.yaml") ||
                schema.Path.Contains($"/{serviceName.Replace("-", "")}-api.yaml"));

            GenerateUnitTestProject(context, serviceName, schemaFile);
        }
    }

    private static void GenerateUnitTestProject(SourceProductionContext context, string serviceName, AdditionalText? schemaFile)
    {
        var testProjectName = $"{serviceName}-unit-tests";
        var className = ToPascalCase(serviceName.Replace("-", ""));

        // Generate project file
        var projectFileContent = GenerateProjectFile(serviceName);
        context.AddSource($"{testProjectName}.csproj", SourceText.From(projectFileContent, Encoding.UTF8));

        // Generate test fixture
        var fixtureContent = GenerateTestFixture(className, serviceName);
        context.AddSource($"{testProjectName}/Fixture.cs", SourceText.From(fixtureContent, Encoding.UTF8));

        // Generate global usings
        var globalUsingsContent = GenerateGlobalUsings();
        context.AddSource($"{testProjectName}/GlobalUsings.cs", SourceText.From(globalUsingsContent, Encoding.UTF8));

        // Generate service-specific tests if schema exists
        if (schemaFile != null)
        {
            var serviceTestsContent = GenerateServiceTests(className, serviceName, schemaFile);
            context.AddSource($"{testProjectName}/{className}Tests.cs", SourceText.From(serviceTestsContent, Encoding.UTF8));
        }
        else
        {
            // Generate basic service tests without schema
            var basicTestsContent = GenerateBasicServiceTests(className, serviceName);
            context.AddSource($"{testProjectName}/{className}Tests.cs", SourceText.From(basicTestsContent, Encoding.UTF8));
        }
    }

    private static string GenerateProjectFile(string serviceName)
    {
        return $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>BeyondImmersion.BannouService.{ToPascalCase(serviceName.Replace("-", ""))}.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.8.0"" />
    <PackageReference Include=""NUnit"" Version=""4.0.1"" />
    <PackageReference Include=""NUnit3TestAdapter"" Version=""4.5.0"" />
    <PackageReference Include=""NUnit.Analyzers"" Version=""3.9.0"">
      <PrivateAssets>all</PrivateAssets>
      <IncludeInTargetFramework>false</IncludeInTargetFramework>
    </PackageReference>
    <PackageReference Include=""coverlet.collector"" Version=""6.0.0"">
      <PrivateAssets>all</PrivateAssets>
      <IncludeInTargetFramework>false</IncludeInTargetFramework>
    </PackageReference>
    <PackageReference Include=""Microsoft.AspNetCore.Mvc.Testing"" Version=""9.0.0"" />
    <PackageReference Include=""Moq"" Version=""4.20.70"" />
    <PackageReference Include=""Dapr.Client"" Version=""1.14.0"" />
    <PackageReference Include=""Microsoft.Extensions.Logging.Abstractions"" Version=""9.0.0"" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include=""../lib-{serviceName}/lib-{serviceName}.csproj"" />
    <ProjectReference Include=""../bannou-service/bannou-service.csproj"" />
    <ProjectReference Include=""../lib-testing-core/lib-testing-core.csproj"" />
  </ItemGroup>

</Project>";
    }

    private static string GenerateGlobalUsings()
    {
        return @"global using NUnit.Framework;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.DependencyInjection;
global using Moq;
global using System;
global using System.Threading.Tasks;
global using BeyondImmersion.BannouService.Testing;";
    }

    private static string GenerateTestFixture(string className, string serviceName)
    {
        return $@"using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.{className}.Tests;

[SetUpFixture]
public class TestFixture
{{
    public static WebApplicationFactory<Program>? Factory {{ get; private set; }}
    public static IServiceProvider? Services {{ get; private set; }}
    public static ILogger<{className}Service>? Logger {{ get; private set; }}

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {{
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {{
                builder.ConfigureAppConfiguration((context, config) =>
                {{
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {{
                        [""{serviceName.ToUpper().Replace('-', '_')}_SERVICE_ENABLED""] = ""true"",
                        [""TESTING_SERVICE_ENABLED""] = ""true"",
                        [""Logging:LogLevel:Default""] = ""Information""
                    }});
                }});

                builder.ConfigureServices((context, services) =>
                {{
                    // Add any test-specific service overrides here
                }});
            }});

        Services = Factory.Services;
        Logger = Services.GetRequiredService<ILogger<{className}Service>>();
    }}

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {{
        Factory?.Dispose();
    }}
}}";
    }

    private static string GenerateServiceTests(string className, string serviceName, AdditionalText schemaFile)
    {
        return $@"using BeyondImmersion.BannouService.{className};

namespace BeyondImmersion.BannouService.{className}.Tests;

[TestFixture]
public class {className}ServiceTests
{{
    private {className}Service _service = null!;
    private Mock<ILogger<{className}Service>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {{
        _mockLogger = new Mock<ILogger<{className}Service>>();

        // Initialize service with dependencies
        _service = new {className}Service(_mockLogger.Object);
    }}

    [TearDown]
    public void TearDown()
    {{
        // Clean up resources if needed
    }}

    [Test]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {{
        // Arrange & Act
        var service = new {className}Service(_mockLogger.Object);

        // Assert
        Assert.That(service, Is.Not.Null);
    }}

    [Test]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {{
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new {className}Service(null!));
    }}

    // TODO: Add schema-specific tests based on OpenAPI specification
    // Schema file: {schemaFile?.Path ?? "Not found"}

    [Test]
    [Category(""Integration"")]
    public async Task Service_ShouldIntegrateWithDaprCorrectly()
    {{
        // This test verifies the service works within the Dapr ecosystem
        // TODO: Implement based on service-specific requirements
        await Task.CompletedTask;
    }}

    [Test]
    [Category(""Performance"")]
    public async Task Service_ShouldMeetPerformanceRequirements()
    {{
        // This test verifies the service meets performance SLAs
        // TODO: Implement based on service-specific requirements
        await Task.CompletedTask;
    }}
}}

[TestFixture]
public class {className}ConfigurationTests
{{
    [Test]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {{
        // Arrange
        var config = new {className}ServiceConfiguration();

        // Act & Assert
        Assert.That(config, Is.Not.Null);
    }}

    // TODO: Add configuration-specific tests
}}";
    }

    private static string GenerateBasicServiceTests(string className, string serviceName)
    {
        return $@"using BeyondImmersion.BannouService.{className};

namespace BeyondImmersion.BannouService.{className}.Tests;

[TestFixture]
public class {className}ServiceTests
{{
    private Mock<ILogger<{className}Service>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {{
        _mockLogger = new Mock<ILogger<{className}Service>>();
    }}

    [Test]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {{
        // Arrange & Act
        var service = new {className}Service(_mockLogger.Object);

        // Assert
        Assert.That(service, Is.Not.Null);
    }}

    [Test]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {{
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new {className}Service(null!));
    }}

    // TODO: Add service-specific tests based on business logic requirements
}}";
    }

    private static string ExtractServiceName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        // Handle lib-service-name format
        if (fileName.StartsWith("lib-"))
            return fileName.Substring(4); // Remove "lib-" prefix

        return string.Empty;
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var parts = input.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var result = string.Join("", parts.Select(part =>
            char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant()));

        return result;
    }
}
