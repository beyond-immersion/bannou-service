using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// Categories of behavior sets for organizational purposes.
/// </summary>
public enum BehaviorSetCategory
{
    Base,
    Cultural,
    Professional,
    Personal,
    Situational
}

/// <summary>
/// Types of validation errors that can occur.
/// </summary>
public enum ValidationErrorType
{
    Syntax,
    Semantic,
    Schema,
    Context,
    ServiceDependency
}

/// <summary>
/// Root ABML document structure for YAML parsing.
/// </summary>
public class AbmlDocument
{
    /// <summary>
    /// ABML specification version.
    /// </summary>
    [YamlMember(Alias = "version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Metadata about this behavior definition.
    /// </summary>
    [YamlMember(Alias = "metadata")]
    public AbmlMetadata? Metadata { get; set; }

    /// <summary>
    /// Context variables and requirements.
    /// </summary>
    [YamlMember(Alias = "context")]
    public AbmlContext? Context { get; set; }

    /// <summary>
    /// Behavior execution blocks.
    /// </summary>
    [YamlMember(Alias = "behaviors")]
    public Dictionary<string, AbmlBehavior> Behaviors { get; set; } = new();
}

/// <summary>
/// ABML document metadata.
/// </summary>
public class AbmlMetadata
{
    /// <summary>
    /// Unique identifier for this behavior definition.
    /// </summary>
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Category of this behavior.
    /// </summary>
    [YamlMember(Alias = "category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Priority level for behavior merging.
    /// </summary>
    [YamlMember(Alias = "priority")]
    public int Priority { get; set; } = 50;

    /// <summary>
    /// Author of this behavior definition.
    /// </summary>
    [YamlMember(Alias = "author")]
    public string? Author { get; set; }

    /// <summary>
    /// Description of this behavior definition.
    /// </summary>
    [YamlMember(Alias = "description")]
    public string? Description { get; set; }
}

/// <summary>
/// ABML context definition for variables and requirements.
/// </summary>
public class AbmlContext
{
    /// <summary>
    /// Context variables and their initial values.
    /// </summary>
    [YamlMember(Alias = "variables")]
    public Dictionary<string, object> Variables { get; set; } = new();

    /// <summary>
    /// Required world state conditions.
    /// </summary>
    [YamlMember(Alias = "requirements")]
    public Dictionary<string, object> Requirements { get; set; } = new();

    /// <summary>
    /// Service dependencies for this behavior.
    /// </summary>
    [YamlMember(Alias = "services")]
    public List<AbmlServiceDependency> Services { get; set; } = new();
}

/// <summary>
/// Service dependency definition in ABML.
/// </summary>
public class AbmlServiceDependency
{
    /// <summary>
    /// Name of the required service.
    /// </summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether this service is required for behavior execution.
    /// </summary>
    [YamlMember(Alias = "required")]
    public bool Required { get; set; } = true;
}

/// <summary>
/// ABML behavior definition with triggers and actions.
/// </summary>
public class AbmlBehavior
{
    /// <summary>
    /// Conditions that trigger this behavior.
    /// </summary>
    [YamlMember(Alias = "triggers")]
    public List<Dictionary<string, object>> Triggers { get; set; } = new();

    /// <summary>
    /// Preconditions that must be met before execution.
    /// </summary>
    [YamlMember(Alias = "preconditions")]
    public List<string> Preconditions { get; set; } = new();

    /// <summary>
    /// Actions to execute when this behavior runs.
    /// </summary>
    [YamlMember(Alias = "actions")]
    public List<Dictionary<string, object>> Actions { get; set; } = new();

    /// <summary>
    /// Goals this behavior contributes to (for GOAP integration).
    /// </summary>
    [YamlMember(Alias = "goals")]
    public List<string> Goals { get; set; } = new();
}

/// <summary>
/// Information about how a behavior set was merged during stack compilation.
/// </summary>
public class BehaviorSetMergeInfo
{
    /// <summary>
    /// ID of the behavior set.
    /// </summary>
    public string BehaviorSetId { get; set; } = string.Empty;

    /// <summary>
    /// Priority level used for merging.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Whether this behavior set took precedence in conflicts.
    /// </summary>
    public bool TookPrecedence { get; set; }

    /// <summary>
    /// List of behaviors that were overridden by this set.
    /// </summary>
    public List<string> OverriddenBehaviors { get; set; } = new();
}

/// <summary>
/// Response model for behavior stack compilation operations.
/// Extends the base response with merge information.
/// </summary>
public class CompileBehaviorStack : CompileBehaviorResponse
{
    /// <summary>
    /// Information about how behavior sets were merged.
    /// </summary>
    public List<BehaviorSetMergeInfo> MergeInfo { get; set; } = new();
}