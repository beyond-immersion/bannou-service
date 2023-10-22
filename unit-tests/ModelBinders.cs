using System.Reflection;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

[Collection("unit tests")]
public class ModelBinders : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    private ModelBinders(CollectionFixture collectionContext) => TestCollectionContext = collectionContext;
    public ModelBinders(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<ModelBinders>();
    }

    [FromHeaderArray()]
    public Dictionary<string, List<string>>? DictionaryListProperty { get; set; }

    [FromHeaderArray()]
    public List<string>? ListProperty { get; set; }

    [FromHeaderArray(Name = "NotTheSame")]
    public Dictionary<string, List<string>>? CustomNameProperty { get; set; }

    [FromHeaderArray(Name = "Different", Delimeter = "@@")]
    public Dictionary<string, List<string>>? CustomNameAndDelimeterProperty { get; set; }

    [Fact]
    public void HeaderArrayModelBinder_DictionaryList()
    {

    }
}
