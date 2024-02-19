[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace BeyondImmersion.BannouService.Behaviour.UnitTests;

public class CollectionFixture : IDisposable
{
    public CollectionFixture() { }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
