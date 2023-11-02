using BeyondImmersion.BannouService.Controllers.Messages;
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

    [HeaderArray]
    public IEnumerable<KeyValuePair<string, IEnumerable<string>>>? EnumerableKVPEnumerableProperty { get; set; }

    [HeaderArray]
    public IEnumerable<KeyValuePair<string, string[]>>? EnumerableKVPArrayProperty { get; set; }

    [HeaderArray]
    public IEnumerable<KeyValuePair<string, List<string>>>? EnumerableKVPListProperty { get; set; }

    [HeaderArray]
    public IEnumerable<KeyValuePair<string, string>>? EnumerableKVPProperty { get; set; }

    [HeaderArray]
    public Dictionary<string, IEnumerable<string>>? DictionaryEnumerableProperty { get; set; }

    [HeaderArray]
    public Dictionary<string, string[]>? DictionaryArrayProperty { get; set; }

    [HeaderArray]
    public Dictionary<string, List<string>>? DictionaryListProperty { get; set; }

    [HeaderArray]
    public Dictionary<string, string>? DictionaryProperty { get; set; }

    [HeaderArray]
    public IEnumerable<(string, IEnumerable<string>)>? EnumerableTupleEnumerableProperty { get; set; }

    [HeaderArray]
    public IEnumerable<(string, string[])>? EnumerableTupleArrayProperty { get; set; }

    [HeaderArray]
    public IEnumerable<(string, List<string>)>? EnumerableTupleListProperty { get; set; }

    [HeaderArray]
    public (string, IEnumerable<string>)[]? ArrayTupleEnumerableProperty { get; set; }

    [HeaderArray]
    public (string, string[])[]? ArrayTupleArrayProperty { get; set; }

    [HeaderArray]
    public (string, List<string>)[]? ArrayTupleListProperty { get; set; }

    [HeaderArray]
    public List<(string, IEnumerable<string>)>? ListTupleEnumerableProperty { get; set; }

    [HeaderArray]
    public List<(string, string[])>? ListTupleArrayProperty { get; set; }

    [HeaderArray]
    public List<(string, List<string>)>? ListTupleListProperty { get; set; }

    [HeaderArray]
    public (string, string)[]? ArrayTupleProperty { get; set; }

    [HeaderArray]
    public List<(string, string)>? ListTupleProperty { get; set; }

    [HeaderArray]
    public IEnumerable<(string, string)>? EnumerableTupleProperty { get; set; }

    [HeaderArray]
    public IEnumerable<string>? EnumerableProperty { get; set; }

    [HeaderArray]
    public string[]? ArrayProperty { get; set; }

    [HeaderArray]
    public List<string>? ListProperty { get; set; }

    [HeaderArray(Delimeter = ";;")]
    public Dictionary<string, List<string>>? CustomDelimeterProperty { get; set; }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableKVPEnumerable()
    {
        var propertyData = GetPropertyData(nameof(EnumerableKVPEnumerableProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        EnumerableKVPEnumerableProperty = (IEnumerable<KeyValuePair<string, IEnumerable<string>>>?)bindResult;
        Assert.Equal("TEST_VALUE_1", EnumerableKVPEnumerableProperty?.FirstOrDefault().Value?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableKVPArray()
    {
        var propertyData = GetPropertyData(nameof(EnumerableKVPArrayProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        EnumerableKVPArrayProperty = (IEnumerable<KeyValuePair<string, string[]>>?)bindResult;
        Assert.Equal("TEST_VALUE_1", EnumerableKVPArrayProperty?.FirstOrDefault().Value?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableKVPList()
    {
        var propertyData = GetPropertyData(nameof(EnumerableKVPListProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        EnumerableKVPListProperty = (IEnumerable<KeyValuePair<string, List<string>>>?)bindResult;
        Assert.Equal("TEST_VALUE_1", EnumerableKVPListProperty?.FirstOrDefault().Value?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableKVP()
    {
        var propertyData = GetPropertyData(nameof(EnumerableKVPProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        EnumerableKVPProperty = (IEnumerable<KeyValuePair<string, string>>?)bindResult;
        Assert.Equal("TEST_KEY_1", EnumerableKVPProperty?.FirstOrDefault().Key);
        Assert.Equal("TEST_VALUE_1", EnumerableKVPProperty?.FirstOrDefault().Value);
    }

    [Fact]
    public void ModelBinders_HeaderArray_DictionaryEnumerable()
    {
        var propertyData = GetPropertyData(nameof(DictionaryEnumerableProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        DictionaryEnumerableProperty = (Dictionary<string, IEnumerable<string>>?)bindResult;
        Assert.Equal("TEST_VALUE_1", DictionaryEnumerableProperty?.FirstOrDefault().Value?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_DictionaryArray()
    {
        var propertyData = GetPropertyData(nameof(DictionaryArrayProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        DictionaryArrayProperty = (Dictionary<string, string[]>?)bindResult;
        Assert.Equal("TEST_VALUE_1", DictionaryArrayProperty?.FirstOrDefault().Value?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_DictionaryList()
    {
        var propertyData = GetPropertyData(nameof(DictionaryListProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        DictionaryListProperty = (Dictionary<string, List<string>>?)bindResult;
        Assert.Equal("TEST_VALUE_1", DictionaryListProperty?.FirstOrDefault().Value?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_Dictionary()
    {
        var propertyData = GetPropertyData(nameof(DictionaryProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        DictionaryProperty = (Dictionary<string, string>?)bindResult;
        Assert.Equal("TEST_KEY_1", DictionaryProperty?.FirstOrDefault().Key);
        Assert.Equal("TEST_VALUE_1", DictionaryProperty?.FirstOrDefault().Value);
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableTupleEnumerable()
    {
        var propertyData = GetPropertyData(nameof(EnumerableTupleEnumerableProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        EnumerableTupleEnumerableProperty = (IEnumerable<(string, IEnumerable<string>)>?)bindResult;
        Assert.Equal("TEST_KEY_1", EnumerableTupleEnumerableProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", EnumerableTupleEnumerableProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableTupleArray()
    {
        var propertyData = GetPropertyData(nameof(EnumerableTupleArrayProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        EnumerableTupleArrayProperty = (IEnumerable<(string, string[])>?)bindResult;
        Assert.Equal("TEST_KEY_1", EnumerableTupleArrayProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", EnumerableTupleArrayProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableTupleList()
    {
        var propertyData = GetPropertyData(nameof(EnumerableTupleListProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        EnumerableTupleListProperty = (IEnumerable<(string, List<string>)>?)bindResult;
        Assert.Equal("TEST_KEY_1", EnumerableTupleListProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", EnumerableTupleListProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_ArrayTupleEnumerable()
    {
        var propertyData = GetPropertyData(nameof(ArrayTupleEnumerableProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        ArrayTupleEnumerableProperty = ((string, IEnumerable<string>)[]?)bindResult;
        Assert.Equal("TEST_KEY_1", ArrayTupleEnumerableProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", ArrayTupleEnumerableProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_ArrayTupleArray()
    {
        var propertyData = GetPropertyData(nameof(ArrayTupleArrayProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        ArrayTupleArrayProperty = ((string, string[])[]?)bindResult;
        Assert.Equal("TEST_KEY_1", ArrayTupleArrayProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", ArrayTupleArrayProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_ArrayTupleList()
    {
        var propertyData = GetPropertyData(nameof(ArrayTupleListProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        ArrayTupleListProperty = ((string, List<string>)[]?)bindResult;
        Assert.Equal("TEST_KEY_1", ArrayTupleListProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", ArrayTupleListProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_ListTupleEnumerable()
    {
        var propertyData = GetPropertyData(nameof(ListTupleEnumerableProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        ListTupleEnumerableProperty = (List<(string, IEnumerable<string>)>?)bindResult;
        Assert.Equal("TEST_KEY_1", ListTupleEnumerableProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", ListTupleEnumerableProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_ListTupleArray()
    {
        var propertyData = GetPropertyData(nameof(ListTupleArrayProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        ListTupleArrayProperty = (List<(string, string[])>?)bindResult;
        Assert.Equal("TEST_KEY_1", ListTupleArrayProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", ListTupleArrayProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_ListTupleList()
    {
        var propertyData = GetPropertyData(nameof(ListTupleListProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        ListTupleListProperty = (List<(string, List<string>)>?)bindResult;
        Assert.Equal("TEST_KEY_1", ListTupleListProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", ListTupleListProperty?.FirstOrDefault().Item2.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_EnumerableTuple()
    {
        var propertyData = GetPropertyData(nameof(EnumerableTupleProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        EnumerableTupleProperty = (IEnumerable<(string, string)>?)bindResult;
        Assert.Equal("TEST_KEY_1", EnumerableTupleProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", EnumerableTupleProperty?.FirstOrDefault().Item2);
    }

    [Fact]
    public void ModelBinders_HeaderArray_ArrayTuple()
    {
        var propertyData = GetPropertyData(nameof(ArrayTupleProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        ArrayTupleProperty = ((string, string)[]?)bindResult;
        Assert.Equal("TEST_KEY_1", ArrayTupleProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", ArrayTupleProperty?.FirstOrDefault().Item2);
    }

    [Fact]
    public void ModelBinders_HeaderArray_ListTuple()
    {
        var propertyData = GetPropertyData(nameof(ListTupleProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        ListTupleProperty = (List<(string, string)>?)bindResult;
        Assert.Equal("TEST_KEY_1", ListTupleProperty?.FirstOrDefault().Item1);
        Assert.Equal("TEST_VALUE_1", ListTupleProperty?.FirstOrDefault().Item2);
    }

    [Fact]
    public void ModelBinders_HeaderArray_Enumerable()
    {
        var propertyData = GetPropertyData(nameof(EnumerableProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        EnumerableProperty = (IEnumerable<string>?)bindResult;
        Assert.Equal("TEST_KEY_1__TEST_VALUE_1", EnumerableProperty?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_Array()
    {
        var propertyData = GetPropertyData(nameof(ArrayProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        ArrayProperty = (string[]?)bindResult;
        Assert.Equal("TEST_KEY_1__TEST_VALUE_1", ArrayProperty?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_List()
    {
        var propertyData = GetPropertyData(nameof(ListProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1__TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        ListProperty = (List<string>?)bindResult;
        Assert.Equal("TEST_KEY_1__TEST_VALUE_1", ListProperty?.FirstOrDefault());
    }

    [Fact]
    public void ModelBinders_HeaderArray_CustomDelimeter()
    {
        var propertyData = GetPropertyData(nameof(CustomDelimeterProperty));
        Assert.NotNull(propertyData);

        var headers = new[] { "TEST_KEY_1;;TEST_VALUE_1" };
        var bindResult = ServiceMessage.BuildPropertyValueFromHeaders(propertyData.Value.Item1.PropertyType, headers, propertyData.Value.Item2);
        Assert.NotNull(bindResult);
        CustomDelimeterProperty = (Dictionary<string, List<string>>?)bindResult;
        Assert.Equal("TEST_VALUE_1", CustomDelimeterProperty?["TEST_KEY_1"].FirstOrDefault());
    }

    private (PropertyInfo, HeaderArrayAttribute)? GetPropertyData(string propertyName)
    {
        var propertyInfo = GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        var propertyAttr = propertyInfo?.GetCustomAttribute<HeaderArrayAttribute>();

        if (propertyInfo == null || propertyAttr == null)
            return null;

        return (propertyInfo, propertyAttr);
    }
}
