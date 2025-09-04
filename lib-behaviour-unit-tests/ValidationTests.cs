using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Reflection;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Behaviour.UnitTests;

[Collection("behaviour schema validation unit tests")]
public class ValidationTests : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }
    private string ProjectPath { get; }
    private string BehaviourPath { get; }
    private string PrerequisitesPath { get; }

    public ValidationTests(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<ValidationTests>();

        ProjectPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "./", "../../..");
        BehaviourPath = Path.Combine(ProjectPath, "Behaviours/");
        PrerequisitesPath = Path.Combine(ProjectPath, "Prerequisites/");
    }

    [Fact]
    public void ValidatePrerequisite_Item_Basic()
    {
        var prereqStr = "{ \"type\": \"item\", \"name\": \"Water Bottle\" }";
        var prereqObj = JObject.Parse(prereqStr);

        Assert.True(BehaviourService.IsValidPrerequite(prereqStr, out IList<string> validationErrors));
        Assert.Empty(validationErrors);

        Assert.True(BehaviourService.IsValidPrerequite(prereqObj, out validationErrors));
        Assert.Empty(validationErrors);
    }

    [Fact]
    public void ValidatePrerequisite_Item_WithCount()
    {
        var prereqStr = "{ \"type\": \"item\", \"name\": \"Water Bottle\", \"value\": 10 }";
        var prereqObj = JObject.Parse(prereqStr);

        Assert.True(BehaviourService.IsValidPrerequite(prereqStr, out IList<string> validationErrors));
        Assert.Empty(validationErrors);

        Assert.True(BehaviourService.IsValidPrerequite(prereqObj, out validationErrors));
        Assert.Empty(validationErrors);
    }

    [Fact]
    public async Task ValidatePrerequisite_ImportTestJSONFromFiles()
    {
        (string, JObject)[] filename__JsonObjs = await LoadJSONTestObjectsFromDirectory(PrerequisitesPath);

        Assert.NotEmpty(filename__JsonObjs);

        foreach ((string, JObject) filename__JsonObj in filename__JsonObjs)
        {
            Program.Logger.LogInformation($"Running [{filename__JsonObj.Item1}] prerequisite test");

            Assert.Equal(ShouldBeValid(filename__JsonObj.Item1), BehaviourService.IsValidPrerequite(filename__JsonObj.Item2, out IList<string> validationErrors));

            if (ShouldBeValid(filename__JsonObj.Item1))
                Assert.Empty(validationErrors);
        }
    }

    [Fact]
    public async Task ValidateBehaviour_ImportTestJSONFromFiles()
    {
        (string, JObject)[] filename__JsonObjs = await LoadJSONTestObjectsFromDirectory(BehaviourPath);

        //Assert.NotEmpty(shouldBeValid__JsonObjs);

        foreach ((string, JObject) filename__JsonObj in filename__JsonObjs)
        {
            Program.Logger.LogInformation($"Running [{filename__JsonObj.Item1}] behaviour test");

            Assert.Equal(ShouldBeValid(filename__JsonObj.Item1), BehaviourService.IsValidBehaviour(filename__JsonObj.Item2, out IList<string> validationErrors));

            if (ShouldBeValid(filename__JsonObj.Item1))
                Assert.Empty(validationErrors);
        }
    }

    /// <summary>
    /// Returns a tuple- the first item is the filename, and the second item is the Json in the test file, as a JObject.
    /// 
    /// Invalid JSON files simply won't be included in the results, but each will throw a parse error.
    /// Should always return at least an empty list, not null.
    /// </summary>
    private static async Task<(string, JObject)[]> LoadJSONTestObjectsFromDirectory(string directory)
    {
        Assert.NotEmpty(directory);

        var resultList = new List<(string, JObject)>();

        foreach (var filepath in Directory.EnumerateFiles(directory))
        {
            if (File.Exists(filepath))
            {
                var fileName = Path.GetFileName(filepath);

                var fileJson = await LoadJSONFromFile(filepath);
                if (fileJson == null)
                    continue;

                resultList.Add((fileName, fileJson));
            }
        }

        return [.. resultList];
    }

    private static async Task<JObject?> LoadJSONFromFile(string filename)
    {
        var contents = await File.ReadAllTextAsync(filename);
        if (string.IsNullOrWhiteSpace(contents))
            return null;

        JObject? jsonObj = null;
        try
        {
            jsonObj = JObject.Parse(contents);
        }
        catch
        {
            Program.Logger.LogError("Could not parse file contents- JSON invalid.");
        }

        return jsonObj;
    }

    private static bool ShouldBeValid(string filename)
    {
        return !filename.StartsWith("invalid_", StringComparison.InvariantCultureIgnoreCase);
    }
}
