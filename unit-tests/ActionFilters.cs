using BeyondImmersion.BannouService.Controllers.Filters;
using BeyondImmersion.BannouService.Controllers.Messages;
using System.Reflection;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

[Collection("unit tests")]
public class ActionFilters : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public ActionFilters(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<ActionFilters>();
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
    public IEnumerable<(string, List<string>)>? EnumerableTupleListProperty { get; set; }

    [HeaderArray]
    public IEnumerable<(string, string[])>? EnumerableTupleArrayProperty { get; set; }

    [HeaderArray]
    public (string, IEnumerable<string>)[]? ArrayTupleEnumerableProperty { get; set; }

    [HeaderArray]
    public (string, List<string>)[]? ArrayTupleListProperty { get; set; }

    [HeaderArray]
    public (string, string[])[]? ArrayTupleArrayProperty { get; set; }

    [HeaderArray]
    public List<(string, IEnumerable<string>)>? ListTupleEnumerableProperty { get; set; }

    [HeaderArray]
    public List<(string, List<string>)>? ListTupleListProperty { get; set; }

    [HeaderArray]
    public List<(string, string[])>? ListTupleArrayProperty { get; set; }

    [HeaderArray]
    public IEnumerable<(string, string)>? EnumerableTupleProperty { get; set; }

    [HeaderArray]
    public (string, string)[]? ArrayTupleProperty { get; set; }

    [HeaderArray]
    public List<(string, string)>? ListTupleProperty { get; set; }

    [HeaderArray]
    public IEnumerable<string>? EnumerableProperty { get; set; }

    [HeaderArray]
    public string[]? ArrayProperty { get; set; }

    [HeaderArray]
    public List<string>? ListProperty { get; set; }

    [HeaderArray(Name = "Different", Delimeter = "@@")]
    public Dictionary<string, List<string>>? CustomNameAndDelimeterProperty { get; set; }

    [HeaderArray(Name = "NotTheSame")]
    public Dictionary<string, List<string>>? CustomNameProperty { get; set; }

    [HeaderArray(Delimeter = ";;")]
    public Dictionary<string, List<string>>? CustomDelimeterProperty { get; set; }

    [Fact]
    public void ActionFilters_HeaderArray_CustomNameAndDelimeter()
    {
        var propertyName = nameof(CustomNameAndDelimeterProperty);

        // test null value
        CustomNameAndDelimeterProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        CustomNameAndDelimeterProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.DoesNotContain(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => string.Equals("Different", t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1@@TEST_VALUE_1"));

        // test one key, two values
        CustomNameAndDelimeterProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.DoesNotContain(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => string.Equals("Different", t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1@@TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1@@TEST_VALUE_2"));

        // test two keys, one value each
        CustomNameAndDelimeterProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1" } },
            { "TEST_KEY_2", new() { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.DoesNotContain(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => string.Equals("Different", t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1@@TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2@@TEST_VALUE_2"));

        // test two keys, different number of values
        CustomNameAndDelimeterProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } },
            { "TEST_KEY_2", new() { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.DoesNotContain(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => string.Equals("Different", t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1@@TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2@@TEST_VALUE_2"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1@@TEST_VALUE_2"));

        // test back to null property value (no caching)
        CustomNameAndDelimeterProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_CustomName()
    {
        var propertyName = nameof(CustomNameProperty);

        // test null value
        CustomNameProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        CustomNameProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.DoesNotContain(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => string.Equals("NotTheSame", t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));

        // test one key, two values
        CustomNameProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.DoesNotContain(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => string.Equals("NotTheSame", t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, one value each
        CustomNameProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1" } },
            { "TEST_KEY_2", new() { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.DoesNotContain(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => string.Equals("NotTheSame", t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test two keys, different number of values
        CustomNameProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } },
            { "TEST_KEY_2", new() { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.DoesNotContain(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => string.Equals("NotTheSame", t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        CustomNameProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_CustomDelimeter()
    {
        var propertyName = nameof(CustomDelimeterProperty);

        // test null value
        CustomDelimeterProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        CustomDelimeterProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1;;TEST_VALUE_1"));

        // test one key, two values
        CustomDelimeterProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1;;TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1;;TEST_VALUE_2"));

        // test two keys, one value each
        CustomDelimeterProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1" } },
            { "TEST_KEY_2", new() { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1;;TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2;;TEST_VALUE_2"));

        // test two keys, different number of values
        CustomDelimeterProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } },
            { "TEST_KEY_2", new() { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1;;TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2;;TEST_VALUE_2"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1;;TEST_VALUE_2"));

        // test back to null property value (no caching)
        CustomDelimeterProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_EnumerableKVPEnumerable()
    {
        var propertyName = nameof(EnumerableKVPEnumerableProperty);

        // test null value
        EnumerableKVPEnumerableProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        EnumerableKVPEnumerableProperty = new Dictionary<string, IEnumerable<string>>()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        EnumerableKVPEnumerableProperty = new Dictionary<string, IEnumerable<string>>()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        EnumerableKVPEnumerableProperty = new Dictionary<string, IEnumerable<string>>()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1" } },
            { "TEST_KEY_2", new[] { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        EnumerableKVPEnumerableProperty = new Dictionary<string, IEnumerable<string>>()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } },
            { "TEST_KEY_2", new[] { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        EnumerableKVPEnumerableProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_EnumerableKVPList()
    {
        var propertyName = nameof(EnumerableKVPListProperty);

        // test null value
        EnumerableKVPListProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        EnumerableKVPListProperty = new Dictionary<string, List<string>>()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        EnumerableKVPListProperty = new Dictionary<string, List<string>>()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        EnumerableKVPListProperty = new Dictionary<string, List<string>>()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1" } },
            { "TEST_KEY_2", new() { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        EnumerableKVPListProperty = new Dictionary<string, List<string>>()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } },
            { "TEST_KEY_2", new() { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        EnumerableKVPListProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_EnumerableKVPArray()
    {
        var propertyName = nameof(EnumerableKVPArrayProperty);

        // test null value
        EnumerableKVPArrayProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        EnumerableKVPArrayProperty = new Dictionary<string, string[]>()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        EnumerableKVPArrayProperty = new Dictionary<string, string[]>()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        EnumerableKVPArrayProperty = new Dictionary<string, string[]>()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1" } },
            { "TEST_KEY_2", new[] { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        EnumerableKVPArrayProperty = new Dictionary<string, string[]>()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } },
            { "TEST_KEY_2", new[] { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        EnumerableKVPArrayProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_EnumerableKVP()
    {
        var propertyName = nameof(EnumerableKVPProperty);

        // test null value
        EnumerableKVPProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key/value pair
        EnumerableKVPProperty = new Dictionary<string, string>()
        {
            { "TEST_KEY_1", "TEST_VALUE_1" }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test two key/value pairs
        EnumerableKVPProperty = new Dictionary<string, string>()
        {
            { "TEST_KEY_1", "TEST_VALUE_1" },
            { "TEST_KEY_2", "TEST_VALUE_1" }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test back to null property value (no caching)
        EnumerableKVPProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_DictionaryEnumerable()
    {
        var propertyName = nameof(DictionaryEnumerableProperty);

        // test null value
        DictionaryEnumerableProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        DictionaryEnumerableProperty = new()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        DictionaryEnumerableProperty = new()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        DictionaryEnumerableProperty = new()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1" } },
            { "TEST_KEY_2", new[] { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        DictionaryEnumerableProperty = new()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } },
            { "TEST_KEY_2", new[] { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        DictionaryEnumerableProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_DictionaryArray()
    {
        var propertyName = nameof(DictionaryArrayProperty);

        // test null value
        DictionaryArrayProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        DictionaryArrayProperty = new()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        DictionaryArrayProperty = new()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        DictionaryArrayProperty = new()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1" } },
            { "TEST_KEY_2", new[] { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        DictionaryArrayProperty = new()
        {
            { "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } },
            { "TEST_KEY_2", new[] { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        DictionaryArrayProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_DictionaryList()
    {
        var propertyName = nameof(DictionaryListProperty);

        // test null value
        DictionaryListProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        DictionaryListProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        DictionaryListProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        DictionaryListProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1" } },
            { "TEST_KEY_2", new() { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        DictionaryListProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } },
            { "TEST_KEY_2", new() { "TEST_VALUE_2" } },
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        DictionaryListProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_Dictionary()
    {
        var propertyName = nameof(DictionaryProperty);

        // test null value
        DictionaryProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key/value pair
        DictionaryProperty = new()
        {
            { "TEST_KEY_1", "TEST_VALUE_1" }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test two key/value pairs
        DictionaryProperty = new()
        {
            { "TEST_KEY_1", "TEST_VALUE_1" },
            { "TEST_KEY_2", "TEST_VALUE_1" }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test back to null property value (no caching)
        DictionaryProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_EnumerableTupleEnumerable()
    {
        var propertyName = nameof(EnumerableTupleEnumerableProperty);

        // test null value
        EnumerableTupleEnumerableProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        EnumerableTupleEnumerableProperty = new (string, IEnumerable<string>)[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        EnumerableTupleEnumerableProperty = new (string, IEnumerable<string>)[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        EnumerableTupleEnumerableProperty = new (string, IEnumerable<string>)[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        EnumerableTupleEnumerableProperty = new (string, IEnumerable<string>)[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        EnumerableTupleEnumerableProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_EnumerableTupleList()
    {
        var propertyName = nameof(EnumerableTupleListProperty);

        // test null value
        EnumerableTupleListProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        EnumerableTupleListProperty = new (string, List<string>)[]
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        EnumerableTupleListProperty = new (string, List<string>)[]
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        EnumerableTupleListProperty = new (string, List<string>)[]
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1" } ),
            ( "TEST_KEY_2", new() { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        EnumerableTupleListProperty = new (string, List<string>)[]
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } ),
            ( "TEST_KEY_2", new() { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        EnumerableTupleListProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_EnumerableTupleArray()
    {
        var propertyName = nameof(EnumerableTupleArrayProperty);

        // test null value
        EnumerableTupleArrayProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        EnumerableTupleArrayProperty = new (string, string[])[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        EnumerableTupleArrayProperty = new (string, string[])[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        EnumerableTupleArrayProperty = new (string, string[])[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        EnumerableTupleArrayProperty = new (string, string[])[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        EnumerableTupleArrayProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_ArrayTupleEnumerable()
    {
        var propertyName = nameof(ArrayTupleEnumerableProperty);

        // test null value
        ArrayTupleEnumerableProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        ArrayTupleEnumerableProperty = new (string, IEnumerable<string>)[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        ArrayTupleEnumerableProperty = new (string, IEnumerable<string>)[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        ArrayTupleEnumerableProperty = new (string, IEnumerable<string>)[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        ArrayTupleEnumerableProperty = new (string, IEnumerable<string>)[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        ArrayTupleEnumerableProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_ArrayTupleList()
    {
        var propertyName = nameof(ArrayTupleListProperty);

        // test null value
        ArrayTupleListProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        ArrayTupleListProperty = new (string, List<string>)[]
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        ArrayTupleListProperty = new (string, List<string>)[]
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        ArrayTupleListProperty = new (string, List<string>)[]
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1" } ),
            ( "TEST_KEY_2", new() { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        ArrayTupleListProperty = new (string, List<string>)[]
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } ),
            ( "TEST_KEY_2", new() { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        ArrayTupleListProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_ArrayTupleArray()
    {
        var propertyName = nameof(ArrayTupleArrayProperty);

        // test null value
        ArrayTupleArrayProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        ArrayTupleArrayProperty = new (string, string[])[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        ArrayTupleArrayProperty = new (string, string[])[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        ArrayTupleArrayProperty = new (string, string[])[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        ArrayTupleArrayProperty = new (string, string[])[]
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        ArrayTupleArrayProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_ListTupleEnumerable()
    {
        var propertyName = nameof(ListTupleEnumerableProperty);

        // test null value
        ListTupleEnumerableProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        ListTupleEnumerableProperty = new()
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        ListTupleEnumerableProperty = new()
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        ListTupleEnumerableProperty = new()
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        ListTupleEnumerableProperty = new()
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        ListTupleEnumerableProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_ListTupleList()
    {
        var propertyName = nameof(ListTupleListProperty);

        // test null value
        ListTupleListProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        ListTupleListProperty = new()
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        ListTupleListProperty = new()
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        ListTupleListProperty = new()
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1" } ),
            ( "TEST_KEY_2", new() { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        ListTupleListProperty = new()
        {
            ( "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } ),
            ( "TEST_KEY_2", new() { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        ListTupleListProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_ListTupleArray()
    {
        var propertyName = nameof(ListTupleArrayProperty);

        // test null value
        ListTupleArrayProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key, one value
        ListTupleArrayProperty = new()
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test one key, two values
        ListTupleArrayProperty = new()
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));

        // test two keys, one value each
        ListTupleArrayProperty = new()
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test two keys, different number of values
        ListTupleArrayProperty = new()
        {
            ( "TEST_KEY_1", new[] { "TEST_VALUE_1", "TEST_VALUE_2" } ),
            ( "TEST_KEY_2", new[] { "TEST_VALUE_2" } ),
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));

        // test back to null property value (no caching)
        ListTupleArrayProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_TupleEnumerable()
    {
        var propertyName = nameof(EnumerableTupleProperty);

        // test null value
        EnumerableTupleProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key/value pair
        EnumerableTupleProperty = new[]
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test two key/value pairs
        EnumerableTupleProperty = new[]
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" ),
            ( "TEST_KEY_2", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test back to null property value (no caching)
        EnumerableTupleProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_TupleArray()
    {
        var propertyName = nameof(ArrayTupleProperty);

        // test null value
        ArrayTupleProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key/value pair
        ArrayTupleProperty = new[]
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test two key/value pairs
        ArrayTupleProperty = new[]
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" ),
            ( "TEST_KEY_2", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test back to null property value (no caching)
        ArrayTupleProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_TupleList()
    {
        var propertyName = nameof(ListTupleProperty);

        // test null value
        ListTupleProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key/value pair
        ListTupleProperty = new()
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test two key/value pairs
        ListTupleProperty = new()
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" ),
            ( "TEST_KEY_2", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test back to null property value (no caching)
        ListTupleProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_Enumerable()
    {
        var propertyName = nameof(EnumerableProperty);

        // test null value
        EnumerableProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one value
        EnumerableProperty = new[]
        {
            "TEST_KEY_1__:__TEST_VALUE_1"
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_2"));

        // test two values
        EnumerableProperty = new[]
        {
            "TEST_KEY_1__:__TEST_VALUE_1",
            "TEST_KEY_2__:__TEST_VALUE_1"
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_2"));

        // test back to null property value (no caching)
        EnumerableProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_Array()
    {
        var propertyName = nameof(ArrayProperty);

        // test null value
        ArrayProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one value
        ArrayProperty = new[]
        {
            "TEST_KEY_1__:__TEST_VALUE_1"
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_2"));

        // test two values
        ArrayProperty = new[]
        {
            "TEST_KEY_1__:__TEST_VALUE_1",
            "TEST_KEY_2__:__TEST_VALUE_1"
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_2"));

        // test back to null property value (no caching)
        ArrayProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_List()
    {
        var propertyName = nameof(ListProperty);

        // test null value
        ListProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one value
        ListProperty = new()
        {
            "TEST_KEY_1__:__TEST_VALUE_1"
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_2"));

        // test two values
        ListProperty = new()
        {
            "TEST_KEY_1__:__TEST_VALUE_1",
            "TEST_KEY_2__:__TEST_VALUE_1"
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = ApiRequest.SetHeaderArrayPropertyToHeaders(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.Contains(headerArray, t => string.Equals(propertyName, t.Item1));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_2"));

        // test back to null property value (no caching)
        ListProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    private (PropertyInfo, object, HeaderArrayAttribute)? GetPropertyData(string propertyName)
    {
        var propertyInfo = GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        var propertyValue = propertyInfo?.GetValue(this);
        var propertyAttr = propertyInfo?.GetCustomAttribute<HeaderArrayAttribute>();

        if (propertyInfo == null || propertyValue == null || propertyAttr == null)
            return null;

        return (propertyInfo, propertyValue, propertyAttr);
    }
}
