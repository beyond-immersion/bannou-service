// =============================================================================
// Behavior Model Cache
// Caches interpreter instances per character/type/variant combination.
// =============================================================================

using BeyondImmersion.Bannou.Client.Behavior.Intent;
using BeyondImmersion.Bannou.Client.Behavior.Runtime;
using System.Collections.Concurrent;

namespace BeyondImmersion.Bannou.Client.Behavior;

/// <summary>
/// Caches behavior model interpreters per (character, type, variant) combination.
/// Supports fallback chains for variant selection.
/// </summary>
/// <remarks>
/// <para>
/// Models are registered by type and variant name (e.g., "sword-and-shield" combat).
/// When requesting an interpreter, the cache checks the fallback chain to find
/// the best available variant.
/// </para>
/// <para>
/// Example fallback chain for combat:
/// ["sword-and-shield", "one-handed", "default"]
/// If character requests "sword-and-shield" and it's not registered, falls back
/// to "one-handed", then "default".
/// </para>
/// <para>
/// Thread-safety: The cache uses ConcurrentDictionary for thread-safe access.
/// Individual interpreter instances are NOT thread-safe - each character should
/// have its own interpreter.
/// </para>
/// </remarks>
public sealed class BehaviorModelCache
{
    private readonly ConcurrentDictionary<ModelRegistrationKey, BehaviorModel> _registeredModels = new();
    private readonly ConcurrentDictionary<CacheKey, CachedInterpreter> _cache = new();
    private readonly ConcurrentDictionary<BehaviorModelType, string[]> _fallbackChains = new();

    /// <summary>
    /// Default variant name used when no variant is specified.
    /// </summary>
    public const string DefaultVariant = "default";

    // =========================================================================
    // MODEL REGISTRATION
    // =========================================================================

    /// <summary>
    /// Registers a behavior model for a specific type and variant.
    /// </summary>
    /// <param name="type">The behavior model type.</param>
    /// <param name="variant">The variant name (e.g., "sword-and-shield").</param>
    /// <param name="model">The compiled behavior model.</param>
    public void RegisterModel(BehaviorModelType type, string variant, BehaviorModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        variant ??= DefaultVariant;

        var key = new ModelRegistrationKey(type, variant);
        _registeredModels[key] = model;
    }

    /// <summary>
    /// Unregisters a behavior model.
    /// </summary>
    /// <param name="type">The behavior model type.</param>
    /// <param name="variant">The variant name.</param>
    /// <returns>True if the model was removed, false if not found.</returns>
    public bool UnregisterModel(BehaviorModelType type, string variant)
    {
        variant ??= DefaultVariant;
        var key = new ModelRegistrationKey(type, variant);
        return _registeredModels.TryRemove(key, out _);
    }

    /// <summary>
    /// Gets a registered model by type and variant.
    /// </summary>
    /// <param name="type">The behavior model type.</param>
    /// <param name="variant">The variant name.</param>
    /// <returns>The model if registered, null otherwise.</returns>
    public BehaviorModel? GetModel(BehaviorModelType type, string? variant = null)
    {
        variant ??= DefaultVariant;
        var key = new ModelRegistrationKey(type, variant);
        return _registeredModels.TryGetValue(key, out var model) ? model : null;
    }

    // =========================================================================
    // FALLBACK CHAINS
    // =========================================================================

    /// <summary>
    /// Sets the fallback chain for variant selection.
    /// </summary>
    /// <param name="type">The behavior model type.</param>
    /// <param name="variants">Ordered list of variant names to try.</param>
    /// <remarks>
    /// <para>
    /// Example: SetFallbackChain(Combat, ["sword-and-shield", "one-handed", "default"])
    /// </para>
    /// <para>
    /// When GetInterpreter is called with variant "sword-and-shield" but it's not
    /// registered, the cache will try "one-handed", then "default".
    /// </para>
    /// </remarks>
    public void SetFallbackChain(BehaviorModelType type, string[] variants)
    {
        ArgumentNullException.ThrowIfNull(variants);
        _fallbackChains[type] = variants;
    }

    /// <summary>
    /// Gets the fallback chain for a behavior model type.
    /// </summary>
    /// <param name="type">The behavior model type.</param>
    /// <returns>The fallback chain, or a single-element array with "default".</returns>
    public string[] GetFallbackChain(BehaviorModelType type)
    {
        return _fallbackChains.TryGetValue(type, out var chain)
            ? chain
            : new[] { DefaultVariant };
    }

    // =========================================================================
    // INTERPRETER CACHING
    // =========================================================================

    /// <summary>
    /// Gets or creates an interpreter for a character/type/variant combination.
    /// </summary>
    /// <param name="characterId">Unique identifier for the character.</param>
    /// <param name="type">The behavior model type.</param>
    /// <param name="variant">The preferred variant (null for default).</param>
    /// <returns>The interpreter, or null if no matching model is registered.</returns>
    public BehaviorModelInterpreter? GetInterpreter(
        Guid characterId,
        BehaviorModelType type,
        string? variant = null)
    {
        variant ??= DefaultVariant;

        // Check cache first
        var cacheKey = new CacheKey(characterId, type, variant);
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached.Interpreter;
        }

        // Find the best matching model using fallback chain
        var model = FindBestModel(type, variant);
        if (model == null)
            return null;

        // Create and cache the interpreter
        var interpreter = new BehaviorModelInterpreter(model);
        _cache[cacheKey] = new CachedInterpreter(interpreter, variant);

        return interpreter;
    }

    /// <summary>
    /// Invalidates the cached interpreter for a specific character/type combination.
    /// </summary>
    /// <param name="characterId">Unique identifier for the character.</param>
    /// <param name="type">The behavior model type to invalidate.</param>
    public void Invalidate(Guid characterId, BehaviorModelType type)
    {
        // Remove all cached interpreters for this character/type (any variant)
        var keysToRemove = _cache.Keys
            .Where(k => k.CharacterId == characterId && k.Type == type)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Invalidates all cached interpreters for a character.
    /// </summary>
    /// <param name="characterId">Unique identifier for the character.</param>
    public void InvalidateAll(Guid characterId)
    {
        var keysToRemove = _cache.Keys
            .Where(k => k.CharacterId == characterId)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Clears the entire cache (e.g., on scene unload).
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Gets the number of cached interpreters.
    /// </summary>
    public int CacheCount => _cache.Count;

    /// <summary>
    /// Gets the number of registered models.
    /// </summary>
    public int ModelCount => _registeredModels.Count;

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    /// <summary>
    /// Finds the best matching model using the fallback chain.
    /// </summary>
    private BehaviorModel? FindBestModel(BehaviorModelType type, string preferredVariant)
    {
        // Try preferred variant first
        var model = GetModel(type, preferredVariant);
        if (model != null)
            return model;

        // Try fallback chain
        var fallbackChain = GetFallbackChain(type);
        foreach (var variant in fallbackChain)
        {
            model = GetModel(type, variant);
            if (model != null)
                return model;
        }

        return null;
    }

    // =========================================================================
    // INTERNAL TYPES
    // =========================================================================

    private readonly record struct ModelRegistrationKey(BehaviorModelType Type, string Variant);

    private readonly record struct CacheKey(Guid CharacterId, BehaviorModelType Type, string Variant);

    private readonly record struct CachedInterpreter(BehaviorModelInterpreter Interpreter, string ResolvedVariant);
}
