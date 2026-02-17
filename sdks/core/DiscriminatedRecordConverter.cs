using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.Core;

/// <summary>
/// Base JSON converter for discriminated record hierarchies. Reads a string discriminator
/// property from the JSON object, maps it to a concrete sealed record type, and deserializes
/// accordingly. Write serializes the concrete runtime type directly (the discriminator
/// property is included naturally as a record positional parameter).
/// </summary>
/// <typeparam name="TBase">The abstract record base type.</typeparam>
/// <remarks>
/// <para>
/// Subclass this converter and pass the discriminator property name and type map
/// to the base constructor. Apply <c>[JsonConverter(typeof(YourConverter))]</c>
/// on the abstract record base type.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// [JsonConverter(typeof(ShapeConverter))]
/// public abstract record Shape(string Type);
/// public sealed record Circle(float Radius) : Shape("circle");
/// public sealed record Square(float Side) : Shape("square");
///
/// public class ShapeConverter() : DiscriminatedRecordConverter&lt;Shape&gt;("type",
///     new Dictionary&lt;string, Type&gt;
///     {
///         ["circle"] = typeof(Circle),
///         ["square"] = typeof(Square),
///     });
/// </code>
/// </para>
/// <para>
/// The discriminator property is matched case-insensitively during deserialization,
/// consistent with <see cref="BannouJson.Options"/>. Discriminator values are matched
/// exactly (case-sensitive) since they are machine identifiers.
/// </para>
/// </remarks>
public abstract class DiscriminatedRecordConverter<TBase> : JsonConverter<TBase>
    where TBase : class
{
    private readonly string _discriminatorProperty;
    private readonly IReadOnlyDictionary<string, Type> _typeMap;
    private readonly ConcurrentDictionary<JsonSerializerOptions, JsonSerializerOptions> _optionsCache = new(
        ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Initializes the converter with a discriminator property name and a mapping
    /// from discriminator string values to concrete types.
    /// </summary>
    /// <param name="discriminatorProperty">
    /// The JSON property name used as the type discriminator (e.g., "type").
    /// Matched case-insensitively during deserialization.
    /// </param>
    /// <param name="typeMap">
    /// Maps discriminator values to their corresponding concrete types.
    /// All mapped types must be assignable to <typeparamref name="TBase"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="discriminatorProperty"/> or <paramref name="typeMap"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when any mapped type is not assignable to <typeparamref name="TBase"/>.
    /// </exception>
    protected DiscriminatedRecordConverter(
        string discriminatorProperty,
        IReadOnlyDictionary<string, Type> typeMap)
    {
        ArgumentNullException.ThrowIfNull(discriminatorProperty);
        ArgumentNullException.ThrowIfNull(typeMap);

        foreach (var (key, type) in typeMap)
        {
            if (!typeof(TBase).IsAssignableFrom(type))
            {
                throw new ArgumentException(
                    $"Type '{type.Name}' for discriminator '{key}' is not assignable to {typeof(TBase).Name}",
                    nameof(typeMap));
            }
        }

        _discriminatorProperty = discriminatorProperty;
        _typeMap = typeMap;
    }

    /// <inheritdoc />
    public override TBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject token, got {reader.TokenType}");
        }

        // Parse into a document to peek at the discriminator without consuming the reader position.
        // JsonDocument.ParseValue advances the reader past the entire object.
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var discriminatorValue = FindDiscriminator(root);

        if (!_typeMap.TryGetValue(discriminatorValue, out var concreteType))
        {
            throw new JsonException(
                $"Unknown {typeof(TBase).Name} discriminator value '{discriminatorValue}'. " +
                $"Known values: {string.Join(", ", _typeMap.Keys)}");
        }

        // Deserialize as the concrete type using options without this converter to prevent
        // infinite recursion (the [JsonConverter] attribute on TBase would re-enter this converter).
        var sanitizedOptions = GetOptionsWithoutSelf(options);
        return (TBase?)root.Deserialize(concreteType, sanitizedOptions);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
    {
        // Serialize as the concrete runtime type. The discriminator property is included
        // naturally because it's a positional parameter on the record type.
        var sanitizedOptions = GetOptionsWithoutSelf(options);
        JsonSerializer.Serialize(writer, value, value.GetType(), sanitizedOptions);
    }

    /// <summary>
    /// Scans the JSON object for the discriminator property (case-insensitive).
    /// </summary>
    private string FindDiscriminator(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, _discriminatorProperty, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.GetString()
                    ?? throw new JsonException(
                        $"Discriminator property '{_discriminatorProperty}' is null on {typeof(TBase).Name}");
            }
        }

        throw new JsonException(
            $"Missing required discriminator property '{_discriminatorProperty}' on {typeof(TBase).Name}");
    }

    /// <summary>
    /// Returns a copy of the given options with this converter removed to prevent
    /// infinite recursion. Cached per options instance (typically one: BannouJson.Options).
    /// </summary>
    private JsonSerializerOptions GetOptionsWithoutSelf(JsonSerializerOptions options)
    {
        return _optionsCache.GetOrAdd(options, opts =>
        {
            var copy = new JsonSerializerOptions(opts);
            for (var i = copy.Converters.Count - 1; i >= 0; i--)
            {
                if (copy.Converters[i].GetType() == GetType())
                {
                    copy.Converters.RemoveAt(i);
                }
            }
            return copy;
        });
    }
}
