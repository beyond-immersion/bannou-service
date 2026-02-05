// Copyright (c) Beyond Immersion. All rights reserved.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeyondImmersion.Bannou.StorylineTheory;

/// <summary>
/// Utility for loading YAML data files embedded in the assembly.
/// </summary>
public static class YamlLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Loads and deserializes a YAML file from embedded resources.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="resourceName">The resource file name (e.g., "narrative-state.yaml").</param>
    /// <returns>The deserialized object.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the resource cannot be found.</exception>
    public static T Load<T>(string resourceName)
    {
        var assembly = typeof(YamlLoader).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

        if (fullName is null)
        {
            var available = string.Join(", ", assembly.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. Available resources: {available}");
        }

        using var stream = assembly.GetManifestResourceStream(fullName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Could not open stream for resource '{fullName}'.");
        }

        using var reader = new StreamReader(stream);
        return Deserializer.Deserialize<T>(reader);
    }

    /// <summary>
    /// Loads and deserializes a YAML file from a file path.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="filePath">The path to the YAML file.</param>
    /// <returns>The deserialized object.</returns>
    public static T LoadFromFile<T>(string filePath)
    {
        using var reader = new StreamReader(filePath);
        return Deserializer.Deserialize<T>(reader);
    }
}
