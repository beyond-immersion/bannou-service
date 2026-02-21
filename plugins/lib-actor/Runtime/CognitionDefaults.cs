namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Default cognition template mappings for actor categories.
/// Used as the final fallback when neither actor template config
/// nor ABML metadata specifies a cognition template.
/// </summary>
public static class CognitionDefaults
{
    private static readonly Dictionary<string, string> CategoryTemplateMap = new()
    {
        ["npc-brain"] = "humanoid-cognition-base",
        ["event-combat"] = "creature-cognition-base",
        ["event-regional"] = "creature-cognition-base",
        ["world-admin"] = "object-cognition-base"
    };

    /// <summary>
    /// Gets the default cognition template ID for the given actor category.
    /// Returns null if no default is defined (e.g., "scheduled-task").
    /// </summary>
    /// <param name="category">The actor category identifier.</param>
    /// <returns>The default cognition template ID, or null if no pipeline should be used.</returns>
    public static string? GetDefaultTemplateId(string category)
    {
        return CategoryTemplateMap.GetValueOrDefault(category);
    }
}
