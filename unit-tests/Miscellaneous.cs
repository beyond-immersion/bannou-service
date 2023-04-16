using Xunit.Abstractions;

namespace BeyondImmersion.UnitTests;

[Collection("unit tests")]
public class Miscellaneous : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    [Obsolete(message: "Test message")]
    private readonly bool ObsoleteTestField = true;

    [Obsolete]
    private bool ObsoleteTestProperty { get; set; } = true;

    [Obsolete]
    private bool ObsoleteTestMethod() => true;

    private Miscellaneous(CollectionFixture collectionContext) => TestCollectionContext = collectionContext;

    public Miscellaneous(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Miscellaneous>();
    }

    [Fact]
    public void RemoveAccents()
    {
        Assert.Equal("Oceano", "Océano".RemoveAccent());
        Assert.Equal("Malmo", "Malmö".RemoveAccent());
        Assert.Equal("Dusseldorf", "Düsseldorf".RemoveAccent());
    }

    [Fact]
    public void GenerateWebsafeSlugs()
    {
        Assert.Equal("basic", "Basic".GenerateSlug());
        Assert.Equal("test-service", "Test Service".GenerateSlug());
        Assert.Equal("youll-never-believe-this-one", "You'll never believe this one".GenerateSlug());
    }

    [Fact]
    public void GenerateWebsafeSlugs_45CharacterLimit()
    {
        var testParagraph = "This is just a really long meaningless paragraph to test the slug length.";
        var testResult = "this-is-just-a-really-long-meaningless-paragr";

        Assert.Equal(45, testResult.Length);
        Assert.Equal(testResult, testParagraph.GenerateSlug());
    }

#pragma warning disable CS0612 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete

    [Fact]
    public void ObsoleteTest_Field()
    {
        System.Reflection.FieldInfo? obsMemberInfo = GetType().GetField(nameof(ObsoleteTestField), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(obsMemberInfo);
        Assert.True(obsMemberInfo.IsObsolete());
    }

    [Fact]
    public void ObsoleteTest_Property()
    {
        System.Reflection.PropertyInfo? obsMemberInfo = GetType().GetProperty(nameof(ObsoleteTestProperty), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(obsMemberInfo);
        Assert.True(obsMemberInfo.IsObsolete());
    }

    [Fact]
    public void ObsoleteTest_Method()
    {
        System.Reflection.MethodInfo? obsMemberInfo = GetType().GetMethod(nameof(ObsoleteTestMethod), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(obsMemberInfo);
        Assert.True(obsMemberInfo.IsObsolete());
    }

    [Fact]
    public void ObsoleteTest_GetMessage()
    {
        System.Reflection.FieldInfo? obsMemberInfo = GetType().GetField(nameof(ObsoleteTestField), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(obsMemberInfo);

        _ = obsMemberInfo.IsObsolete(out var message);
        Assert.Equal("Test message", message);
    }

#pragma warning restore CS0612 // Type or member is obsolete
#pragma warning restore CS0618 // Type or member is obsolete

}
