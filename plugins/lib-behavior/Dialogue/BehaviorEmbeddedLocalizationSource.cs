// =============================================================================
// Behavior Embedded Localization Source
// Default ILocalizationSource for lib-behavior — loads YAML strings from
// embedded resources within the lib-behavior assembly.
// =============================================================================

using System.Reflection;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.Behavior.Dialogue;

/// <summary>
/// Default embedded YAML localization source registered with the lib-behavior
/// plugin. Loads from
/// <c>BeyondImmersion.Bannou.Behavior.Localization.strings.{locale}.yaml</c>
/// embedded resources baked into the lib-behavior assembly.
/// </summary>
/// <remarks>
/// <para>
/// Priority 50 — service-backed sources (e.g.,
/// <c>LocalizationServiceSource</c> from lib-localization at priority 100)
/// override these embedded defaults when both are loaded into the
/// <see cref="FileLocalizationProvider"/> aggregate. When no service-backed
/// source is present, this source serves as the fallback for embedded /
/// sidecar deployments without a centralized localization service.
/// </para>
/// <para>
/// If lib-behavior ships with no embedded localization YAML, this source is
/// effectively a no-op: <see cref="EmbeddedYamlLocalizationSource.GetText"/>
/// returns <c>null</c> for every key and <see cref="EmbeddedYamlLocalizationSource.SupportedLocales"/>
/// is empty. Drop a <c>Localization/strings.en.yaml</c> file into the
/// lib-behavior project and add an <c>&lt;EmbeddedResource&gt;</c> entry in
/// the .csproj to enable.
/// </para>
/// </remarks>
[BannouHelperService(
    "behavior-embedded-localization",
    typeof(IBehaviorService),
    typeof(ILocalizationSource),
    lifetime: ServiceLifetime.Singleton)]
public sealed class BehaviorEmbeddedLocalizationSource : EmbeddedYamlLocalizationSource
{
    /// <summary>
    /// Creates a new behavior embedded localization source.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation on async methods.</param>
    public BehaviorEmbeddedLocalizationSource(
        ILogger<BehaviorEmbeddedLocalizationSource> logger,
        ITelemetryProvider telemetryProvider)
        : base(logger, telemetryProvider)
    {
    }

    /// <inheritdoc />
    public override string Name => "behavior-embedded";

    /// <inheritdoc />
    public override int Priority => 50;

    /// <inheritdoc />
    protected override Assembly ResourceAssembly =>
        typeof(BehaviorEmbeddedLocalizationSource).Assembly;

    /// <inheritdoc />
    protected override string ResourcePrefix =>
        "BeyondImmersion.Bannou.Behavior.Localization.";
}
