using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace BeyondImmersion.Bannou.Core.Tests;

/// <summary>
/// Unit tests for <see cref="DiscriminatedRecordConverter{TBase}"/>.
/// Uses test record hierarchies to verify polymorphic JSON round-trip behavior.
/// </summary>
public class DiscriminatedRecordConverterTests
{
    #region Test Record Hierarchy

    /// <summary>
    /// Abstract base record with "type" discriminator.
    /// </summary>
    [JsonConverter(typeof(TestShapeConverter))]
    private abstract record Shape(string Type);

    /// <summary>
    /// Concrete subtype with a single required field.
    /// </summary>
    private sealed record Circle(float Radius) : Shape("circle");

    /// <summary>
    /// Concrete subtype with a single required field.
    /// </summary>
    private sealed record Square(float Side) : Shape("square");

    /// <summary>
    /// Concrete subtype with nullable fields.
    /// </summary>
    private sealed record Rectangle(float Width, float? Height) : Shape("rectangle");

    /// <summary>
    /// Concrete subtype with a nested enum.
    /// </summary>
    private sealed record Polygon(int Sides, PolygonKind Kind) : Shape("polygon");

    /// <summary>
    /// Enum for testing enum serialization within discriminated records.
    /// </summary>
    private enum PolygonKind
    {
        Regular,
        Irregular
    }

    /// <summary>
    /// Test converter mapping shape discriminator values to concrete types.
    /// </summary>
    private class TestShapeConverter() : DiscriminatedRecordConverter<Shape>("type",
        new Dictionary<string, Type>
        {
            ["circle"] = typeof(Circle),
            ["square"] = typeof(Square),
            ["rectangle"] = typeof(Rectangle),
            ["polygon"] = typeof(Polygon),
        });

    #endregion

    #region Deserialization Tests

    /// <summary>
    /// Verifies basic deserialization of each concrete subtype.
    /// </summary>
    [Fact]
    public void Deserialize_Circle_ReturnsCorrectType()
    {
        var json = """{"type":"circle","radius":5.0}""";

        var shape = BannouJson.Deserialize<Shape>(json);

        var circle = Assert.IsType<Circle>(shape);
        Assert.Equal(5.0f, circle.Radius);
        Assert.Equal("circle", circle.Type);
    }

    /// <summary>
    /// Verifies deserialization of a subtype with a different discriminator.
    /// </summary>
    [Fact]
    public void Deserialize_Square_ReturnsCorrectType()
    {
        var json = """{"type":"square","side":10.5}""";

        var shape = BannouJson.Deserialize<Shape>(json);

        var square = Assert.IsType<Square>(shape);
        Assert.Equal(10.5f, square.Side);
    }

    /// <summary>
    /// Verifies deserialization preserves nullable fields when present.
    /// </summary>
    [Fact]
    public void Deserialize_Rectangle_WithNullableField()
    {
        var json = """{"type":"rectangle","width":3.0,"height":4.0}""";

        var shape = BannouJson.Deserialize<Shape>(json);

        var rect = Assert.IsType<Rectangle>(shape);
        Assert.Equal(3.0f, rect.Width);
        Assert.Equal(4.0f, rect.Height);
    }

    /// <summary>
    /// Verifies deserialization handles omitted nullable fields.
    /// </summary>
    [Fact]
    public void Deserialize_Rectangle_WithOmittedNullableField()
    {
        var json = """{"type":"rectangle","width":3.0}""";

        var shape = BannouJson.Deserialize<Shape>(json);

        var rect = Assert.IsType<Rectangle>(shape);
        Assert.Equal(3.0f, rect.Width);
        Assert.Null(rect.Height);
    }

    /// <summary>
    /// Verifies that nested enums are deserialized correctly within discriminated records.
    /// </summary>
    [Fact]
    public void Deserialize_Polygon_WithEnum()
    {
        var json = """{"type":"polygon","sides":6,"kind":"Regular"}""";

        var shape = BannouJson.Deserialize<Shape>(json);

        var polygon = Assert.IsType<Polygon>(shape);
        Assert.Equal(6, polygon.Sides);
        Assert.Equal(PolygonKind.Regular, polygon.Kind);
    }

    #endregion

    #region Case Insensitivity Tests

    /// <summary>
    /// Verifies that the discriminator property name is matched case-insensitively.
    /// </summary>
    [Fact]
    public void Deserialize_CaseInsensitiveDiscriminatorProperty()
    {
        var json = """{"Type":"circle","Radius":2.0}""";

        var shape = BannouJson.Deserialize<Shape>(json);

        Assert.IsType<Circle>(shape);
    }

    /// <summary>
    /// Verifies PascalCase discriminator property works.
    /// </summary>
    [Fact]
    public void Deserialize_PascalCaseDiscriminatorProperty()
    {
        var json = """{"TYPE":"square","SIDE":7.0}""";

        var shape = BannouJson.Deserialize<Shape>(json);

        var square = Assert.IsType<Square>(shape);
        Assert.Equal(7.0f, square.Side);
    }

    /// <summary>
    /// Verifies that the discriminator VALUE is case-sensitive (machine identifiers).
    /// </summary>
    [Fact]
    public void Deserialize_CaseSensitiveDiscriminatorValue_ThrowsOnMismatch()
    {
        var json = """{"type":"Circle","radius":5.0}""";

        var ex = Assert.Throws<JsonException>(() => BannouJson.Deserialize<Shape>(json));
        Assert.Contains("Unknown", ex.Message);
        Assert.Contains("Circle", ex.Message);
    }

    #endregion

    #region Error Handling Tests

    /// <summary>
    /// Verifies that missing discriminator throws with a clear message.
    /// </summary>
    [Fact]
    public void Deserialize_MissingDiscriminator_Throws()
    {
        var json = """{"radius":5.0}""";

        var ex = Assert.Throws<JsonException>(() => BannouJson.Deserialize<Shape>(json));
        Assert.Contains("Missing required discriminator property", ex.Message);
        Assert.Contains("type", ex.Message);
    }

    /// <summary>
    /// Verifies that null discriminator value throws.
    /// </summary>
    [Fact]
    public void Deserialize_NullDiscriminator_Throws()
    {
        var json = """{"type":null,"radius":5.0}""";

        var ex = Assert.Throws<JsonException>(() => BannouJson.Deserialize<Shape>(json));
        Assert.Contains("null", ex.Message);
    }

    /// <summary>
    /// Verifies that unknown discriminator value throws with known values listed.
    /// </summary>
    [Fact]
    public void Deserialize_UnknownDiscriminator_ThrowsWithKnownValues()
    {
        var json = """{"type":"triangle","base":3.0}""";

        var ex = Assert.Throws<JsonException>(() => BannouJson.Deserialize<Shape>(json));
        Assert.Contains("Unknown", ex.Message);
        Assert.Contains("triangle", ex.Message);
        Assert.Contains("circle", ex.Message);
        Assert.Contains("square", ex.Message);
    }

    #endregion

    #region Serialization Tests

    /// <summary>
    /// Verifies that serialization includes the discriminator property.
    /// </summary>
    [Fact]
    public void Serialize_IncludesDiscriminator()
    {
        Shape shape = new Circle(5.0f);

        var json = BannouJson.Serialize(shape);

        Assert.Contains("circle", json);
    }

    /// <summary>
    /// Verifies that serialization includes all concrete type properties.
    /// </summary>
    [Fact]
    public void Serialize_IncludesConcreteProperties()
    {
        Shape shape = new Square(10.5f);

        var json = BannouJson.Serialize(shape);

        Assert.Contains("10.5", json);
    }

    /// <summary>
    /// Verifies that null properties are omitted (BannouJson convention).
    /// </summary>
    [Fact]
    public void Serialize_OmitsNullProperties()
    {
        Shape shape = new Rectangle(3.0f, null);

        var json = BannouJson.Serialize(shape);

        Assert.DoesNotContain("null", json.ToLower());
    }

    /// <summary>
    /// Verifies enum values within discriminated records serialize as strings.
    /// </summary>
    [Fact]
    public void Serialize_EnumAsString()
    {
        Shape shape = new Polygon(6, PolygonKind.Irregular);

        var json = BannouJson.Serialize(shape);

        Assert.Contains("Irregular", json);
        Assert.DoesNotContain(":1", json);
    }

    #endregion

    #region Round-Trip Tests

    /// <summary>
    /// Verifies full round-trip for each concrete subtype.
    /// </summary>
    [Theory]
    [InlineData("""{"type":"circle","radius":5.0}""")]
    [InlineData("""{"type":"square","side":10.5}""")]
    [InlineData("""{"type":"rectangle","width":3.0,"height":4.0}""")]
    [InlineData("""{"type":"polygon","sides":6,"kind":"Regular"}""")]
    public void RoundTrip_PreservesData(string json)
    {
        var deserialized = BannouJson.Deserialize<Shape>(json);
        Assert.NotNull(deserialized);

        var serialized = BannouJson.Serialize(deserialized);
        var roundTripped = BannouJson.Deserialize<Shape>(serialized);

        Assert.NotNull(roundTripped);
        Assert.Equal(deserialized, roundTripped);
    }

    /// <summary>
    /// Verifies round-trip of a list containing mixed subtypes.
    /// </summary>
    [Fact]
    public void RoundTrip_MixedList()
    {
        var shapes = new List<Shape>
        {
            new Circle(1.0f),
            new Square(2.0f),
            new Rectangle(3.0f, 4.0f),
            new Polygon(5, PolygonKind.Regular),
        };

        var json = BannouJson.Serialize(shapes);
        var deserialized = BannouJson.Deserialize<List<Shape>>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(4, deserialized.Count);
        Assert.IsType<Circle>(deserialized[0]);
        Assert.IsType<Square>(deserialized[1]);
        Assert.IsType<Rectangle>(deserialized[2]);
        Assert.IsType<Polygon>(deserialized[3]);
        Assert.Equal(shapes, deserialized);
    }

    #endregion

    #region Constructor Validation Tests

    /// <summary>
    /// Verifies that the constructor rejects types not assignable to TBase.
    /// </summary>
    [Fact]
    public void Constructor_RejectsNonAssignableType()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new BadConverter());
        Assert.Contains("not assignable", ex.Message);
    }

    /// <summary>
    /// Verifies that null discriminator property throws.
    /// </summary>
    [Fact]
    public void Constructor_RejectsNullDiscriminator()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NullDiscriminatorConverter());
    }

    /// <summary>
    /// Verifies that null type map throws.
    /// </summary>
    [Fact]
    public void Constructor_RejectsNullTypeMap()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NullTypeMapConverter());
    }

    /// <summary>
    /// Converter that maps to a type not assignable to Shape.
    /// </summary>
    private class BadConverter() : DiscriminatedRecordConverter<Shape>("type",
        new Dictionary<string, Type> { ["bad"] = typeof(string) });

    /// <summary>
    /// Converter with null discriminator property name.
    /// </summary>
    private class NullDiscriminatorConverter : DiscriminatedRecordConverter<Shape>
    {
        public NullDiscriminatorConverter() : base(null!, new Dictionary<string, Type>()) { }
    }

    /// <summary>
    /// Converter with null type map.
    /// </summary>
    private class NullTypeMapConverter : DiscriminatedRecordConverter<Shape>
    {
        public NullTypeMapConverter() : base("type", null!) { }
    }

    #endregion

    #region Discriminator Position Tests

    /// <summary>
    /// Verifies that the discriminator doesn't need to be the first property.
    /// </summary>
    [Fact]
    public void Deserialize_DiscriminatorNotFirstProperty()
    {
        var json = """{"radius":5.0,"type":"circle"}""";

        var shape = BannouJson.Deserialize<Shape>(json);

        var circle = Assert.IsType<Circle>(shape);
        Assert.Equal(5.0f, circle.Radius);
    }

    #endregion
}
