[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace BeyondImmersion.BannouService.UnitTests;

public class CollectionFixture : IDisposable
{
    public CollectionFixture() { }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
