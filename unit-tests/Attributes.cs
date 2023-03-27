namespace BeyondImmersion.UnitTests;

[Collection("core tests")]
public class Attributes : IClassFixture<Fixture>
{
    private Fixture TestFixture { get; }

    public Attributes(Fixture testFixture)
    {
        TestFixture = testFixture;
    }
}
