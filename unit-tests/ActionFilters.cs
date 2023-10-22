using System.Reflection;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

[Collection("unit tests")]
public class ActionFilters : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    private ActionFilters(CollectionFixture collectionContext) => TestCollectionContext = collectionContext;
    public ActionFilters(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<ActionFilters>();
    }

    [ToHeaderArray]
    public IEnumerable<KeyValuePair<string, IEnumerable<string>>>? EnumerableKVPEnumerableProperty { get; set; }

    [ToHeaderArray]
    public IEnumerable<KeyValuePair<string, string[]>>? EnumerableKVPArrayProperty { get; set; }

    [ToHeaderArray]
    public IEnumerable<KeyValuePair<string, List<string>>>? EnumerableKVPListProperty { get; set; }

    [ToHeaderArray]
    public IEnumerable<KeyValuePair<string, string>>? EnumerableKVPProperty { get; set; }

    [ToHeaderArray]
    public Dictionary<string, IEnumerable<string>>? DictionaryEnumerableProperty { get; set; }

    [ToHeaderArray]
    public Dictionary<string, string[]>? DictionaryArrayProperty { get; set; }

    [ToHeaderArray]
    public Dictionary<string, List<string>>? DictionaryListProperty { get; set; }

    [ToHeaderArray]
    public Dictionary<string, string>? DictionaryProperty { get; set; }

    [ToHeaderArray]
    public IEnumerable<(string, string)>? TupleEnumerableProperty { get; set; }

    [ToHeaderArray]
    public (string, string)[]? TupleArrayProperty { get; set; }

    [ToHeaderArray]
    public List<(string, string)>? TupleListProperty { get; set; }

    [ToHeaderArray]
    public IEnumerable<string>? EnumerableProperty { get; set; }

    [ToHeaderArray]
    public string[]? ArrayProperty { get; set; }

    [ToHeaderArray]
    public List<string>? ListProperty { get; set; }

    [ToHeaderArray(Name = "Different", Delimeter = "@@")]
    public Dictionary<string, List<string>>? CustomNameAndDelimeterProperty { get; set; }

    [ToHeaderArray(Name = "NotTheSame")]
    public Dictionary<string, List<string>>? CustomNameProperty { get; set; }

    [ToHeaderArray(Delimeter = ";;")]
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 0);
        Assert.True(headerArray.Count(t => string.Equals("Different", t.Item1)) == 1);
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1@@TEST_VALUE_1"));

        // test one key, two values
        CustomNameAndDelimeterProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 0);
        Assert.True(headerArray.Count(t => string.Equals("Different", t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 0);
        Assert.True(headerArray.Count(t => string.Equals("Different", t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 0);
        Assert.True(headerArray.Count(t => string.Equals("Different", t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 0);
        Assert.True(headerArray.Count(t => string.Equals("NotTheSame", t.Item1)) == 1);
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));

        // test one key, two values
        CustomNameProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 0);
        Assert.True(headerArray.Count(t => string.Equals("NotTheSame", t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 0);
        Assert.True(headerArray.Count(t => string.Equals("NotTheSame", t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 0);
        Assert.True(headerArray.Count(t => string.Equals("NotTheSame", t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1;;TEST_VALUE_1"));

        // test one key, two values
        CustomDelimeterProperty = new()
        {
            { "TEST_KEY_1", new() { "TEST_VALUE_1", "TEST_VALUE_2" } }
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);

        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
    public void ActionFilters_HeaderArray_TupleEnumerable()
    {
        var propertyName = nameof(TupleEnumerableProperty);

        // test null value
        TupleEnumerableProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key/value pair
        TupleEnumerableProperty = new[]
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test two key/value pairs
        TupleEnumerableProperty = new[]
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" ),
            ( "TEST_KEY_2", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test back to null property value (no caching)
        TupleEnumerableProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_TupleArray()
    {
        var propertyName = nameof(TupleArrayProperty);

        // test null value
        TupleArrayProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key/value pair
        TupleArrayProperty = new[]
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test two key/value pairs
        TupleArrayProperty = new[]
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" ),
            ( "TEST_KEY_2", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test back to null property value (no caching)
        TupleArrayProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    [Fact]
    public void ActionFilters_HeaderArray_TupleList()
    {
        var propertyName = nameof(TupleListProperty);

        // test null value
        TupleListProperty = null;
        var propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);

        // test one key/value pair
        TupleListProperty = new()
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test two key/value pairs
        TupleListProperty = new()
        {
            ( "TEST_KEY_1", "TEST_VALUE_1" ),
            ( "TEST_KEY_2", "TEST_VALUE_1" )
        };
        propertyData = GetPropertyData(propertyName);
        Assert.NotNull(propertyData);
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__TEST_VALUE_2"));

        // test back to null property value (no caching)
        TupleListProperty = null;
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        var headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
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
        headerArray = HeaderArrayActionFilter.PropertyValueToHeaderArray(propertyData.Value.Item1, propertyData.Value.Item2, propertyData.Value.Item3);
        Assert.NotNull(headerArray);
        Assert.True(headerArray.Count(t => string.Equals(propertyName, t.Item1)) == 1);
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_1"));
        Assert.Contains(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_1"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_1__:__TEST_VALUE_2"));
        Assert.DoesNotContain(headerArray, t => t.Item2.Contains("TEST_KEY_2__:__TEST_VALUE_2"));

        // test back to null property value (no caching)
        ListProperty = null;
        propertyData = GetPropertyData(propertyName);
        Assert.Null(propertyData);
    }

    private (PropertyInfo, object, ToHeaderArrayAttribute)? GetPropertyData(string propertyName)
    {
        var propertyInfo = GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        var propertyValue = propertyInfo?.GetValue(this);
        var propertyAttr = propertyInfo?.GetCustomAttribute<ToHeaderArrayAttribute>();

        if (propertyInfo == null || propertyValue == null || propertyAttr == null)
            return null;

        return (propertyInfo, propertyValue, propertyAttr);
    }
}
