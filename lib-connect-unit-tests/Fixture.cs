[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace BeyondImmersion.BannouService.Connect.UnitTests;

public class CollectionFixture : IDisposable
{
    public CollectionFixture() { }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
