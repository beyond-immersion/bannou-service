// =============================================================================
// Behavior Seeded Resource Provider
// Exposes embedded ABML behaviors via the lib-resource seeded resource API.
// =============================================================================

using System.Reflection;
using BeyondImmersion.BannouService.Providers;

namespace BeyondImmersion.BannouService.Actor.Providers;

/// <summary>
/// Provides seeded ABML behavior documents via the lib-resource seeded resource API.
/// </summary>
/// <remarks>
/// <para>
/// This provider implements <see cref="ISeededResourceProvider"/> to expose embedded
/// behavior YAML files to the lib-resource service. This enables behaviors to be
/// discovered and loaded via the <c>/resource/seeded/list</c> and <c>/resource/seeded/get</c>
/// endpoints.
/// </para>
/// <para>
/// This is separate from <see cref="SeededBehaviorProvider"/> which implements
/// <see cref="IBehaviorDocumentProvider"/> for the actor runtime's behavior document
/// loading system. Both providers load from the same embedded resources but serve
/// different purposes:
/// <list type="bullet">
///   <item><see cref="SeededBehaviorProvider"/> - Actor runtime behavior loading</item>
///   <item><see cref="BehaviorSeededResourceProvider"/> - lib-resource API exposure</item>
/// </list>
/// </para>
/// </remarks>
public sealed class BehaviorSeededResourceProvider : EmbeddedResourceProvider
{
    /// <inheritdoc />
    public override string ResourceType => "behavior";

    /// <inheritdoc />
    public override string ContentType => "application/yaml";

    /// <inheritdoc />
    protected override Assembly ResourceAssembly => typeof(BehaviorSeededResourceProvider).Assembly;

    /// <inheritdoc />
    protected override string ResourcePrefix => "BeyondImmersion.BannouService.Actor.Behaviors.";

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string> GetMetadata(string identifier, string resourceName)
    {
        // Parse the behavior type from the identifier naming convention
        var behaviorType = identifier switch
        {
            var id when id.Contains("_base") => "base_template",
            var id when id.StartsWith("regional") => "event_brain",
            var id when id.StartsWith("encounter") => "event_brain",
            var id when id.StartsWith("object") => "object_brain",
            _ => "character_brain"
        };

        return new Dictionary<string, string>
        {
            ["behavior_type"] = behaviorType,
            ["source"] = "embedded"
        };
    }
}
