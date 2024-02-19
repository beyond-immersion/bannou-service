using Xunit.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace BeyondImmersion.BannouService.Behaviour.UnitTests;

[Collection("behaviour unit tests")]
public class Schemas : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }
    private string ProjectPath { get; }
    private string BehaviourPath { get; }

    public Schemas(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Schemas>();

        ProjectPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "./", "../../..");
        BehaviourPath = Path.Combine(ProjectPath, "Behaviours/");
    }

    [Fact]
    public void Test_BehaviourTreeSchema()
    {

        var mainSchema = JObject.Parse(File.ReadAllText(Path.Combine(BehaviourPath, "Behaviour.schema")));
        var prerequisiteSchema = mainSchema["definitions"]?["prerequisite"]?.ToString();

        Assert.NotNull(prerequisiteSchema);

        var preReqSchema = JSchema.Parse(prerequisiteSchema);
        var prerequisiteObject = JObject.Parse("{ \"type\": \"item\", \"name\": \"Water Bottle\" }");

        Assert.True(prerequisiteObject.IsValid(preReqSchema, out IList<string> validationErrors));
    }
}
