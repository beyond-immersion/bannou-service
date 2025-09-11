namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Configuration for the ABML behavior service.
/// </summary>
public class BehaviorServiceConfiguration
{
    /// <summary>
    /// Enable behavior caching for compiled ABML definitions.
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Maximum size of the behavior cache.
    /// </summary>
    public int MaxCacheSize { get; set; } = 1000;

    /// <summary>
    /// Enable YAML validation against ABML schema.
    /// </summary>
    public bool EnableValidation { get; set; } = true;
}
