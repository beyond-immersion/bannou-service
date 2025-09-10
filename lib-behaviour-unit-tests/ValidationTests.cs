using Microsoft.Extensions.Logging;
using System.Reflection;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Behaviour.UnitTests;

[Collection("behaviour schema validation unit tests")]
public class ValidationTests : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }
    private string ProjectPath { get; }
    private readonly BehaviorService _behaviorService;

    public ValidationTests(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<ValidationTests>();

        ProjectPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "./", "../../..");
        _behaviorService = new BehaviorService(output.BuildLoggerFor<BehaviorService>());
    }

    [Fact]
    public async Task ValidateAbml_BasicYaml_Success()
    {
        var abmlContent = @"
version: ""1.0.0""
metadata:
  id: ""test_behavior""
  category: ""test""
behaviors:
  test:
    triggers:
      - condition: ""true""
    actions:
      - log:
          message: ""Hello World""
";

        var result = await _behaviorService.ValidateAbml(abmlContent);
        
        Assert.IsType<OkObjectResult>(result.Result);
        var okResult = result.Result as OkObjectResult;
        var response = okResult?.Value as ValidateAbmlResponse;
        
        Assert.NotNull(response);
        Assert.True(response.Is_valid);
    }

    [Fact]
    public async Task ValidateAbml_InvalidYaml_ReturnsFalse()
    {
        var invalidYaml = "invalid: yaml: content: [";

        var result = await _behaviorService.ValidateAbml(invalidYaml);
        
        Assert.IsType<OkObjectResult>(result.Result);
        var okResult = result.Result as OkObjectResult;
        var response = okResult?.Value as ValidateAbmlResponse;
        
        Assert.NotNull(response);
        Assert.False(response.Is_valid);
    }

    [Fact]
    public async Task CompileAbmlBehavior_ValidYaml_ReturnsCompiled()
    {
        var abmlContent = @"
version: ""1.0.0""
metadata:
  id: ""test_compile""
  category: ""test""
behaviors:
  test:
    triggers:
      - condition: ""true""
    actions:
      - log:
          message: ""Test compilation""
";

        var result = await _behaviorService.CompileAbmlBehavior(abmlContent);
        
        Assert.IsType<OkObjectResult>(result.Result);
        var okResult = result.Result as OkObjectResult;
        var response = okResult?.Value as CompileBehaviorResponse;
        
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.Behavior_id);
        Assert.NotNull(response.Compiled_behavior);
    }

    [Fact]
    public async Task GetCachedBehavior_NonExistentId_ReturnsNotFound()
    {
        var result = await _behaviorService.GetCachedBehavior("non-existent-id");
        
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task InvalidateCachedBehavior_NonExistentId_ReturnsNotFound()
    {
        var result = await _behaviorService.InvalidateCachedBehavior("non-existent-id");
        
        Assert.IsType<NotFoundObjectResult>(result);
    }
}