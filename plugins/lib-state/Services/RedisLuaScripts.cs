#nullable enable

using System.Collections.Concurrent;
using System.Reflection;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Loads and caches Redis Lua scripts from embedded resources.
/// Scripts are located in the Scripts/ directory and embedded at build time.
/// </summary>
public static class RedisLuaScripts
{
    private static readonly ConcurrentDictionary<string, string> _scriptCache = new();
    private static readonly Assembly _assembly = typeof(RedisLuaScripts).Assembly;

    /// <summary>
    /// Script for atomic create-if-not-exists operation.
    /// Returns 1 on success, -1 if key already exists.
    /// </summary>
    public static string TryCreate => GetScript("TryCreate");

    /// <summary>
    /// Script for atomic optimistic concurrency update.
    /// Returns new version on success, -1 on version mismatch.
    /// </summary>
    public static string TryUpdate => GetScript("TryUpdate");

    /// <summary>
    /// Gets a Lua script by name from embedded resources.
    /// Scripts are cached after first load.
    /// </summary>
    /// <param name="scriptName">Name of the script (without .lua extension).</param>
    /// <returns>The Lua script content.</returns>
    /// <exception cref="InvalidOperationException">Script not found in embedded resources.</exception>
    public static string GetScript(string scriptName)
    {
        return _scriptCache.GetOrAdd(scriptName, name =>
        {
            // Resource name format: {RootNamespace}.Scripts.{ScriptName}.lua
            var resourceName = $"BeyondImmersion.BannouService.State.Scripts.{name}.lua";

            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // List available resources for debugging
                var available = string.Join(", ", _assembly.GetManifestResourceNames());
                throw new InvalidOperationException(
                    $"Lua script '{name}' not found. Expected resource: {resourceName}. " +
                    $"Available resources: {available}");
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }

    /// <summary>
    /// Lists all available script names (for debugging/testing).
    /// </summary>
    /// <returns>Collection of script names without the .lua extension.</returns>
    public static IEnumerable<string> ListAvailableScripts()
    {
        const string prefix = "BeyondImmersion.BannouService.State.Scripts.";
        const string suffix = ".lua";

        return _assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix) && name.EndsWith(suffix))
            .Select(name => name[prefix.Length..^suffix.Length]);
    }
}
