using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Scene.Helpers;

/// <summary>
/// Validates scene structures and applies game-specific validation rules.
/// Extracted from SceneService for improved testability.
/// </summary>
public class SceneValidationService : ISceneValidationService
{
    // refId validation pattern: must start with lowercase letter, followed by lowercase letters, digits, or underscores
    private static readonly Regex RefIdPattern = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    /// <inheritdoc/>
    public ValidationResult ValidateStructure(Scene scene, int maxNodeCount)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationError>();

        // Validate sceneId is not empty Guid
        if (scene.SceneId == Guid.Empty)
        {
            errors.Add(new ValidationError
            {
                RuleId = "valid-uuid",
                Message = "sceneId must be a valid non-empty UUID",
                Severity = ValidationSeverity.Error
            });
        }

        // Validate version pattern
        if (!string.IsNullOrEmpty(scene.Version) && !Regex.IsMatch(scene.Version, @"^\d+\.\d+\.\d+$"))
        {
            errors.Add(new ValidationError
            {
                RuleId = "valid-version",
                Message = "version must match MAJOR.MINOR.PATCH pattern",
                Severity = ValidationSeverity.Error
            });
        }

        // Validate root node exists
        if (scene.Root == null)
        {
            errors.Add(new ValidationError
            {
                RuleId = "single-root",
                Message = "Scene must have a root node",
                Severity = ValidationSeverity.Error
            });
        }
        else
        {
            // Validate root has no parent
            if (scene.Root.ParentNodeId != null)
            {
                errors.Add(new ValidationError
                {
                    RuleId = "root-no-parent",
                    Message = "Root node must have null parentNodeId",
                    Severity = ValidationSeverity.Error,
                    NodeId = scene.Root.NodeId
                });
            }

            // Validate all nodes
            var allNodes = CollectAllNodes(scene.Root);

            // Check node count limit
            if (allNodes.Count > maxNodeCount)
            {
                errors.Add(new ValidationError
                {
                    RuleId = "node-count-limit",
                    Message = $"Scene exceeds maximum node count of {maxNodeCount}",
                    Severity = ValidationSeverity.Error
                });
            }

            // Check refId uniqueness
            var refIds = new HashSet<string>();
            foreach (var node in allNodes)
            {
                if (string.IsNullOrEmpty(node.RefId))
                {
                    errors.Add(new ValidationError
                    {
                        RuleId = "unique-refid",
                        Message = "Node must have a refId",
                        Severity = ValidationSeverity.Error,
                        NodeId = node.NodeId
                    });
                }
                else if (!RefIdPattern.IsMatch(node.RefId))
                {
                    errors.Add(new ValidationError
                    {
                        RuleId = "refid-pattern",
                        Message = $"refId '{node.RefId}' must match pattern ^[a-z][a-z0-9_]*$",
                        Severity = ValidationSeverity.Error,
                        NodeId = node.NodeId
                    });
                }
                else if (!refIds.Add(node.RefId))
                {
                    errors.Add(new ValidationError
                    {
                        RuleId = "unique-refid",
                        Message = $"Duplicate refId '{node.RefId}'",
                        Severity = ValidationSeverity.Error,
                        NodeId = node.NodeId
                    });
                }

                // Validate nodeId is not empty
                if (node.NodeId == Guid.Empty)
                {
                    errors.Add(new ValidationError
                    {
                        RuleId = "valid-uuid",
                        Message = "nodeId must be a valid non-empty UUID",
                        Severity = ValidationSeverity.Error
                    });
                }

                // Validate transform
                if (node.LocalTransform == null)
                {
                    errors.Add(new ValidationError
                    {
                        RuleId = "valid-transform",
                        Message = "Node must have a localTransform",
                        Severity = ValidationSeverity.Error,
                        NodeId = node.NodeId
                    });
                }
            }
        }

        return new ValidationResult
        {
            Valid = errors.Count == 0,
            Errors = errors.Count > 0 ? errors : null,
            Warnings = warnings.Count > 0 ? warnings : null
        };
    }

    /// <inheritdoc/>
    public ValidationResult ApplyGameValidationRules(Scene scene, IReadOnlyList<ValidationRule>? rules)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationError>();

        if (rules == null || rules.Count == 0)
        {
            return new ValidationResult { Valid = true };
        }

        var allNodes = CollectAllNodes(scene.Root);

        foreach (var rule in rules)
        {
            var ruleErrors = ApplyValidationRule(rule, scene, allNodes);
            foreach (var error in ruleErrors)
            {
                if (rule.Severity == ValidationSeverity.Error)
                {
                    errors.Add(error);
                }
                else
                {
                    warnings.Add(error);
                }
            }
        }

        return new ValidationResult
        {
            Valid = errors.Count == 0,
            Errors = errors.Count > 0 ? errors : null,
            Warnings = warnings.Count > 0 ? warnings : null
        };
    }

    /// <inheritdoc/>
    public void MergeResults(ValidationResult target, ValidationResult source)
    {
        if (source.Errors != null)
        {
            if (target.Errors == null)
            {
                target.Errors = new List<ValidationError>();
            }
            ((List<ValidationError>)target.Errors).AddRange(source.Errors);
            target.Valid = false;
        }

        if (source.Warnings != null)
        {
            if (target.Warnings == null)
            {
                target.Warnings = new List<ValidationError>();
            }
            ((List<ValidationError>)target.Warnings).AddRange(source.Warnings);
        }
    }

    /// <inheritdoc/>
    public List<SceneNode> CollectAllNodes(SceneNode root)
    {
        var nodes = new List<SceneNode>();
        CollectNodesRecursive(root, nodes);
        return nodes;
    }

    /// <summary>
    /// Applies a single validation rule to the scene.
    /// </summary>
    private static List<ValidationError> ApplyValidationRule(ValidationRule rule, Scene scene, List<SceneNode> allNodes)
    {
        var errors = new List<ValidationError>();
        var config = rule.Config;

        switch (rule.RuleType)
        {
            case ValidationRuleType.RequireTag:
                if (!string.IsNullOrEmpty(config?.Tag))
                {
                    var matchingNodes = allNodes.Where(n =>
                        (string.IsNullOrEmpty(config.NodeType) || n.NodeType.ToString() == config.NodeType) &&
                        n.Tags != null && n.Tags.Contains(config.Tag)).ToList();

                    var minCount = config.MinCount ?? 1;
                    if (matchingNodes.Count < minCount)
                    {
                        errors.Add(new ValidationError
                        {
                            RuleId = rule.RuleId,
                            Message = $"Scene requires at least {minCount} nodes with tag '{config.Tag}'",
                            Severity = rule.Severity
                        });
                    }

                    if (config.MaxCount.HasValue && matchingNodes.Count > config.MaxCount.Value)
                    {
                        errors.Add(new ValidationError
                        {
                            RuleId = rule.RuleId,
                            Message = $"Scene has too many nodes with tag '{config.Tag}' (max: {config.MaxCount.Value})",
                            Severity = rule.Severity
                        });
                    }
                }
                break;

            case ValidationRuleType.ForbidTag:
                if (!string.IsNullOrEmpty(config?.Tag))
                {
                    var forbiddenNodes = allNodes.Where(n => n.Tags != null && n.Tags.Contains(config.Tag)).ToList();
                    foreach (var node in forbiddenNodes)
                    {
                        errors.Add(new ValidationError
                        {
                            RuleId = rule.RuleId,
                            Message = $"Tag '{config.Tag}' is forbidden",
                            Severity = rule.Severity,
                            NodeId = node.NodeId
                        });
                    }
                }
                break;

            case ValidationRuleType.RequireNodeType:
                if (!string.IsNullOrEmpty(config?.NodeType))
                {
                    var matchingNodes = allNodes.Where(n => n.NodeType.ToString() == config.NodeType).ToList();
                    var minCount = config.MinCount ?? 1;
                    if (matchingNodes.Count < minCount)
                    {
                        errors.Add(new ValidationError
                        {
                            RuleId = rule.RuleId,
                            Message = $"Scene requires at least {minCount} nodes of type '{config.NodeType}'",
                            Severity = rule.Severity
                        });
                    }
                }
                break;

            case ValidationRuleType.RequireAnnotation:
                errors.Add(new ValidationError
                {
                    RuleId = rule.RuleId,
                    Message = $"Validation rule type '{rule.RuleType}' is not yet implemented; rule was skipped",
                    Severity = ValidationSeverity.Warning
                });
                break;

            case ValidationRuleType.CustomExpression:
                errors.Add(new ValidationError
                {
                    RuleId = rule.RuleId,
                    Message = $"Validation rule type '{rule.RuleType}' is not yet implemented; rule was skipped",
                    Severity = ValidationSeverity.Warning
                });
                break;

            default:
                errors.Add(new ValidationError
                {
                    RuleId = rule.RuleId,
                    Message = $"Unknown validation rule type '{rule.RuleType}'; rule was skipped",
                    Severity = ValidationSeverity.Warning
                });
                break;
        }

        return errors;
    }

    /// <summary>
    /// Recursively collects all nodes in the scene hierarchy.
    /// </summary>
    private static void CollectNodesRecursive(SceneNode node, List<SceneNode> nodes)
    {
        nodes.Add(node);
        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                CollectNodesRecursive(child, nodes);
            }
        }
    }
}
