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

    [FromHeaderArray]
    public Dictionary<string, List<string>>? DictionaryListProperty { get; set; }

    [FromHeaderArray]
    public IEnumerable<string>? EnumerableProperty { get; set; }

    [FromHeaderArray]
    public string[]? ArrayProperty { get; set; }

    [FromHeaderArray]
    public List<string>? ListProperty { get; set; }

    [FromHeaderArray(Delimeter = ";;")]
    public Dictionary<string, List<string>>? CustomDelimeterProperty { get; set; }

    [Fact]
    public void ModelBinders_HeaderArray_DictionaryList()
    {
        var propertyData = GetPropertyData(nameof(DictionaryListProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        DictionaryListProperty = (Dictionary<string, List<string>>?)bindResult.Model;
        Assert.Equal("TEST_VALUE_1", DictionaryListProperty?["TEST_KEY_1"].FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_Enumerable()
    {
        var propertyData = GetPropertyData(nameof(EnumerableProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        EnumerableProperty = (IEnumerable<string>?)bindResult.Model;
        Assert.Equal("TEST_KEY_1__TEST_VALUE_1", EnumerableProperty?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_Array()
    {
        var propertyData = GetPropertyData(nameof(ArrayProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        ArrayProperty = (string[]?)bindResult.Model;
        Assert.Equal("TEST_KEY_1__TEST_VALUE_1", ArrayProperty?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_List()
    {
        var propertyData = GetPropertyData(nameof(ListProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        ListProperty = (List<string>?)bindResult.Model;
        Assert.Equal("TEST_KEY_1__TEST_VALUE_1", ListProperty?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_CustomDelimeter()
    {
        var propertyData = GetPropertyData(nameof(CustomDelimeterProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1;;TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        DictionaryListProperty = (Dictionary<string, List<string>>?)bindResult.Model;
        Assert.Equal("TEST_VALUE_1", DictionaryListProperty?["TEST_KEY_1"].FirstOrDefault());
    }

    private (PropertyInfo, FromHeaderArrayAttribute)? GetPropertyData(string propertyName)
    {
        var propertyInfo = GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        var propertyAttr = propertyInfo?.GetCustomAttribute<FromHeaderArrayAttribute>();

        if (propertyInfo == null || propertyAttr == null)
            return null;

        return (propertyInfo, propertyAttr);
    }
}
