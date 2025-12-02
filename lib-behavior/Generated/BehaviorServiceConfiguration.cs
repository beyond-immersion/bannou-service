using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Configuration class for Behavior service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(BehaviorService), envPrefix: "BANNOU_")]
public class BehaviorServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Maximum number of stackable behaviors per NPC
    /// Environment variable: MAXBEHAVIORSTACKDEPTH or BANNOU_MAXBEHAVIORSTACKDEPTH
    /// </summary>
    public int MaxBehaviorStackDepth { get; set; } = 10;

    /// <summary>
    /// Maximum number of compiled behaviors to cache
    /// Environment variable: COMPILATIONCACHESIZE or BANNOU_COMPILATIONCACHESIZE
    /// </summary>
    public int CompilationCacheSize { get; set; } = 1000;

    /// <summary>
    /// Maximum time allowed for behavior compilation
    /// Environment variable: COMPILATIONTIMEOUTSECONDS or BANNOU_COMPILATIONTIMEOUTSECONDS
    /// </summary>
    public int CompilationTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// ABML validation strictness level
    /// Environment variable: ABMLVALIDATIONLEVEL or BANNOU_ABMLVALIDATIONLEVEL
    /// </summary>
    public string ABMLValidationLevel { get; set; } = "standard";

    /// <summary>
    /// Maximum depth for recursive context variable expansion
    /// Environment variable: CONTEXTVARIABLEEXPANSIONDEPTH or BANNOU_CONTEXTVARIABLEEXPANSIONDEPTH
    /// </summary>
    public int ContextVariableExpansionDepth { get; set; } = 5;

}
