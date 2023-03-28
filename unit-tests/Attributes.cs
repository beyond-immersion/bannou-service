namespace BeyondImmersion.UnitTests;

[Collection("core tests")]
public class Attributes : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public Attributes(CollectionFixture collectionContext)
    {
        TestCollectionContext = collectionContext;
    }
}
