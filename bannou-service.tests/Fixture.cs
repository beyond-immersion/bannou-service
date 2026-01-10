namespace BeyondImmersion.BannouService.Tests;

public class CollectionFixture : IDisposable
{
    public CollectionFixture() { }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
