namespace BeyondImmersion.BannouService;

/// <summary>
/// Provides generic enum-to-enum mapping by name for boundary crossings between
/// schema-generated enums and external SDK/library enums.
/// </summary>
/// <remarks>
/// <para>
/// <b>When to use:</b> At plugin SDK boundaries (A2 pattern) where a schema-generated enum
/// and a domain SDK enum have matching value names but are separate C# types. For example,
/// mapping between a generated <c>ArcType</c> and a StorylineTheory SDK <c>ArcType</c>.
/// </para>
/// <para>
/// <b>When NOT to use:</b> For lossy mappings where source values intentionally map to
/// different target values (e.g., <c>Aeolian -> Minor</c>, <c>Overloaded -> Degraded</c>).
/// Those require explicit switch expressions.
/// </para>
/// <para>
/// <b>Safety guarantee:</b> Every plugin that uses these methods MUST have a corresponding
/// unit test using <c>EnumMappingValidator</c> from test-utilities to verify that all source
/// values have matching names in the target enum. The unit test catches value drift at
/// compile/test time rather than at runtime.
/// </para>
/// </remarks>
public static class EnumMapping
{
    /// <summary>
    /// Maps a source enum value to a target enum value by matching their string names.
    /// Throws <see cref="InvalidOperationException"/> if the source value has no name match
    /// in the target enum.
    /// </summary>
    /// <typeparam name="TSource">The source enum type.</typeparam>
    /// <typeparam name="TTarget">The target enum type.</typeparam>
    /// <param name="source">The source enum value to map.</param>
    /// <returns>The target enum value with the matching name.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no value in <typeparamref name="TTarget"/> matches the name of <paramref name="source"/>.
    /// </exception>
    public static TTarget MapByName<TSource, TTarget>(this TSource source)
        where TSource : struct, Enum
        where TTarget : struct, Enum
    {
        var name = source.ToString();
        if (Enum.TryParse<TTarget>(name, ignoreCase: false, out var result))
            return result;

        throw new InvalidOperationException(
            $"Cannot map {typeof(TSource).Name}.{name} to {typeof(TTarget).Name}: " +
            $"no matching value. Ensure both enums have aligned value names and a " +
            $"unit test validates this mapping with EnumMappingValidator.");
    }

    /// <summary>
    /// Maps a source enum value to a target enum value by matching their string names.
    /// Returns <paramref name="fallback"/> if the source value has no name match in the target enum.
    /// </summary>
    /// <remarks>
    /// Use this for superset-to-subset mappings where the source enum (e.g., an SDK) has values
    /// that intentionally don't exist in the target enum (e.g., schema-generated). The overlapping
    /// values map by name; extras map to the fallback. Pair with
    /// <c>EnumMappingValidator.AssertSupersetToSubsetMapping</c> to catch unexpected new extras.
    /// </remarks>
    /// <typeparam name="TSource">The source (superset) enum type.</typeparam>
    /// <typeparam name="TTarget">The target (subset) enum type.</typeparam>
    /// <param name="source">The source enum value to map.</param>
    /// <param name="fallback">The value to return when no name match exists in the target.</param>
    /// <returns>The target enum value with the matching name, or <paramref name="fallback"/> if none.</returns>
    public static TTarget MapByNameOrDefault<TSource, TTarget>(this TSource source, TTarget fallback)
        where TSource : struct, Enum
        where TTarget : struct, Enum
    {
        if (Enum.TryParse<TTarget>(source.ToString(), ignoreCase: false, out var result))
            return result;

        return fallback;
    }

    /// <summary>
    /// Attempts to map a source enum value to a target enum value by matching their string names.
    /// Returns <c>false</c> if the source value has no name match in the target enum.
    /// </summary>
    /// <typeparam name="TSource">The source enum type.</typeparam>
    /// <typeparam name="TTarget">The target enum type.</typeparam>
    /// <param name="source">The source enum value to map.</param>
    /// <param name="result">
    /// When this method returns <c>true</c>, contains the mapped target value.
    /// When <c>false</c>, contains the default value of <typeparamref name="TTarget"/>.
    /// </param>
    /// <returns><c>true</c> if a matching value was found; otherwise <c>false</c>.</returns>
    public static bool TryMapByName<TSource, TTarget>(this TSource source, out TTarget result)
        where TSource : struct, Enum
        where TTarget : struct, Enum
    {
        return Enum.TryParse(source.ToString(), ignoreCase: false, out result);
    }
}
