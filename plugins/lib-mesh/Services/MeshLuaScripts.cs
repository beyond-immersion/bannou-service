#nullable enable

using System.Collections.Concurrent;
using System.Reflection;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// Loads and caches Mesh Lua scripts from embedded resources.
/// Scripts are located in the Scripts/ directory and embedded at build time.
/// </summary>
public static class MeshLuaScripts
{
    private static readonly ConcurrentDictionary<string, string> _scriptCache = new();
    private static readonly Assembly _assembly = typeof(MeshLuaScripts).Assembly;

    /// <summary>
    /// Script for atomically recording a circuit breaker failure.
    /// Increments failure count and transitions Closed→Open at threshold.
    /// Returns JSON: {"failures": N, "state": "Closed|Open|HalfOpen", "stateChanged": true|false, "openedAt": timestamp|null}
    /// </summary>
    public static string RecordCircuitFailure => GetScript("RecordCircuitFailure");

    /// <summary>
    /// Script for atomically recording a circuit breaker success.
    /// Resets circuit to Closed state and clears failure count.
    /// Returns JSON: {"state": "Closed", "stateChanged": true|false, "previousState": "Closed|Open|HalfOpen"}
    /// </summary>
    public static string RecordCircuitSuccess => GetScript("RecordCircuitSuccess");

    /// <summary>
    /// Script for getting current circuit breaker state.
    /// Auto-transitions Open→HalfOpen if reset timeout has elapsed.
    /// Returns JSON: {"state": "Closed|Open|HalfOpen", "failures": N, "stateChanged": true|false, "openedAt": timestamp|null}
    /// </summary>
    public static string GetCircuitState => GetScript("GetCircuitState");

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
            var resourceName = $"BeyondImmersion.BannouService.Mesh.Scripts.{name}.lua";

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
        const string prefix = "BeyondImmersion.BannouService.Mesh.Scripts.";
        const string suffix = ".lua";

        return _assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix) && name.EndsWith(suffix))
            .Select(name => name[prefix.Length..^suffix.Length]);
    }
}
