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
    public IEnumerable<KeyValuePair<string, IEnumerable<string>>>? EnumerableKVPEnumerableProperty { get; set; }

    [FromHeaderArray]
    public IEnumerable<KeyValuePair<string, string[]>>? EnumerableKVPArrayProperty { get; set; }

    [FromHeaderArray]
    public IEnumerable<KeyValuePair<string, List<string>>>? EnumerableKVPListProperty { get; set; }

    [FromHeaderArray]
    public IEnumerable<KeyValuePair<string, string>>? EnumerableKVPProperty { get; set; }

    [FromHeaderArray]
    public IEnumerable<(string, IEnumerable<string>)>? EnumerableTupleEnumerableProperty { get; set; }

    [FromHeaderArray]
    public IEnumerable<(string, string[])>? EnumerableTupleArrayProperty { get; set; }

    [FromHeaderArray]
    public IEnumerable<(string, List<string>)>? EnumerableTupleListProperty { get; set; }

    [FromHeaderArray]
    public (string, string)[]? ArrayTupleProperty { get; set; }

    [FromHeaderArray]
    public List<(string, string)>? ListTupleProperty { get; set; }

    [FromHeaderArray]
    public IEnumerable<(string, string)>? EnumerableTupleProperty { get; set; }

    [FromHeaderArray]
    public IEnumerable<string>? EnumerableProperty { get; set; }

    [FromHeaderArray]
    public string[]? ArrayProperty { get; set; }

    [FromHeaderArray]
    public List<string>? ListProperty { get; set; }

    [FromHeaderArray(Delimeter = ";;")]
    public Dictionary<string, List<string>>? CustomDelimeterProperty { get; set; }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableKVPEnumerable()
    {
        var propertyData = GetPropertyData(nameof(EnumerableKVPEnumerableProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        EnumerableKVPEnumerableProperty = (IEnumerable<KeyValuePair<string, IEnumerable<string>>>?)bindResult.Model;
        Assert.Equal("TEST_VALUE_1", EnumerableKVPEnumerableProperty?.FirstOrDefault().Value?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableKVPArray()
    {
        var propertyData = GetPropertyData(nameof(EnumerableKVPArrayProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        EnumerableKVPArrayProperty = (IEnumerable<KeyValuePair<string, string[]>>?)bindResult.Model;
        Assert.Equal("TEST_VALUE_1", EnumerableKVPArrayProperty?.FirstOrDefault().Value?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableKVPList()
    {
        var propertyData = GetPropertyData(nameof(EnumerableKVPListProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        EnumerableKVPListProperty = (IEnumerable<KeyValuePair<string, List<string>>>?)bindResult.Model;
        Assert.Equal("TEST_VALUE_1", EnumerableKVPListProperty?.FirstOrDefault().Value?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableKVP()
    {
        var propertyData = GetPropertyData(nameof(EnumerableKVPProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        EnumerableKVPProperty = (IEnumerable<KeyValuePair<string, string>>?)bindResult.Model;
        Assert.Equal("TEST_KEY_1", EnumerableKVPProperty?.FirstOrDefault().Key);
        Assert.Equal("TEST_VALUE_1", EnumerableKVPProperty?.FirstOrDefault().Value);
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableTupleEnumerable()
    {
        var propertyData = GetPropertyData(nameof(EnumerableTupleEnumerableProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        EnumerableTupleEnumerableProperty = (IEnumerable<(string, IEnumerable<string>)>?)bindResult.Model;
        Assert.Equal("TEST_KEY_1", EnumerableTupleEnumerableProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", EnumerableTupleEnumerableProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableTupleArray()
    {
        var propertyData = GetPropertyData(nameof(EnumerableTupleArrayProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        EnumerableTupleArrayProperty = (IEnumerable<(string, string[])>?)bindResult.Model;
        Assert.Equal("TEST_KEY_1", EnumerableTupleArrayProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", EnumerableTupleArrayProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableTupleList()
    {
        var propertyData = GetPropertyData(nameof(EnumerableTupleListProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        EnumerableTupleListProperty = (IEnumerable<(string, List<string>)>?)bindResult.Model;
        Assert.Equal("TEST_KEY_1", EnumerableTupleListProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", EnumerableTupleListProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableTuple()
    {
        var propertyData = GetPropertyData(nameof(EnumerableTupleProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.True(bindResult.IsModelSet);
        EnumerableTupleProperty = (IEnumerable<(string, string)>?)bindResult.Model;
        Assert.Equal("TEST_KEY_1", EnumerableTupleProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", EnumerableTupleProperty?.FirstOrDefault().Item2);
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
        CustomDelimeterProperty = (Dictionary<string, List<string>>?)bindResult.Model;
        Assert.Equal("TEST_VALUE_1", CustomDelimeterProperty?["TEST_KEY_1"].FirstOrDefault());
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
