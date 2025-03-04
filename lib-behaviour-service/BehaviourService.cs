using BeyondImmersion.BannouService.Behaviour.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// Service component responsible for AI behaviour handling.
/// </summary>
[DaprService("behaviour", typeof(IBehaviourService))]
public sealed class BehaviourService : DaprService<BehaviourServiceConfiguration>, IBehaviourService
{
    private static JSchema? _behaviourSchema;
    private static JSchema BehaviourSchema
    {
        get
        {
            _behaviourSchema ??= JSchema.Parse(Schemas.Behaviour);
            return _behaviourSchema;
        }

        set => _behaviourSchema = value;
    }

    private static JSchema? _prereqSchema;
    private static JSchema PrereqSchema
    {
        get
        {
            if (_prereqSchema == null)
            {
                var behaviourObj = JObject.Parse(Schemas.Behaviour);
                var prereqSchemaStr = behaviourObj["definitions"]?["prerequisite"]?.ToString();

                if (prereqSchemaStr == null)
                    throw new NullReferenceException(nameof(prereqSchemaStr));

                _prereqSchema = JSchema.Parse(prereqSchemaStr);
            }

            return _prereqSchema;
        }

        set => _prereqSchema = value;
    }

    async Task IDaprService.OnStartAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Adds a new behaviour tree to the AI behaviour system.
    /// </summary>
    public async Task<ServiceResponse> AddBehaviourTree()
    {
        await Task.CompletedTask;

        return new ServiceResponse(StatusCodes.OK);
    }

    public static bool ResolveBehaviourReferences(JObject behaviour, Dictionary<string, JObject> refLookup)
    {
        var allResolved = true;
        var childTokens = new Stack<JToken>(behaviour.Children());

        while (childTokens.Count > 0)
        {
            JToken childToken = childTokens.Pop();

            if (childToken is JObject childObj && childObj.Count == 1)
            {
                var refString = (string?)childObj.Property("$ref")?.Value;

                if (refString == null)
                    continue;

                if (refLookup.TryGetValue(refString, out JObject? matchedBehaviour))
                {
                    if (matchedBehaviour == null)
                        throw new NullReferenceException(nameof(matchedBehaviour));

                    childObj.Replace(matchedBehaviour.DeepClone());
                }
                else
                {
                    allResolved = false;
                    break;
                }
            }
            else
                foreach (JToken childChildToken in childToken.Children())
                    childTokens.Push(childChildToken);
        }

        return allResolved;
    }

    public static bool ResolveBehavioursAndAddToLookup(JObject[] behaviours, ref Dictionary<string, JObject>? refLookup, out IList<string> validationErrors)
    {
        int lastErrorCount;
        var thisErrorCount = 0;
        var errors = new List<string>();

        refLookup ??= [];

        do
        {
            lastErrorCount = thisErrorCount;
            thisErrorCount = 0;
            errors.Clear();

            foreach (var behaviour in behaviours)
            {
                var resolvedSuccess = ResolveAndValidateBehaviour(behaviour, refLookup, out var newErrors);

                if (!resolvedSuccess)
                {
                    errors.AddRange(newErrors);
                    thisErrorCount++;
                }
                else
                {
                    var name = (string?)behaviour["name"];

                    if (string.IsNullOrWhiteSpace(name))
                        throw new ArgumentNullException(nameof(name));

                    refLookup.Add(name, behaviour);
                }
            }
        }
        while (thisErrorCount > 0 && thisErrorCount < lastErrorCount);
        // keep going as long as more refs are being resolved each time

        validationErrors = errors;

        return thisErrorCount == 0;
    }

    public static bool ResolveAndValidateBehaviour(JObject behaviour, Dictionary<string, JObject> refLookup, out IList<string> validationErrors)
    {
        var resolved = ResolveBehaviourReferences(behaviour, refLookup);
        var name = (string?)behaviour["name"];

        if (!resolved)
        {
            Program.Logger.LogError($"Could not resolve references for behaviour [{name}].");
            validationErrors = new List<string>() { $"Could not resolve all references for behaviour [{name}]" };

            return false;
        }

        return IsValidBehaviour(behaviour, out validationErrors);
    }

    public static bool IsValidBehaviour(JObject behaviour, out IList<string> validationErrors) => behaviour.IsValid(BehaviourSchema, out validationErrors);

    public static bool IsValidBehaviour(string behaviourStr, out IList<string> validationErrors)
    {
        var jsonObj = JObject.Parse(behaviourStr);
        var name = (string?)jsonObj["name"];

        validationErrors = new List<string>();

        var valid = IsValidBehaviour(jsonObj, out var errors);
        if (!valid)
        {
            Program.Logger.LogError($"Invalid behaviour [{name}].");

            var sb = new StringBuilder();
            sb.AppendLine($"Behaviour [{name}] contains validation errors.");
            sb.Append(errors);

            validationErrors.Add(sb.ToString());
        }

        return valid;
    }

    public static bool IsValidPrerequite(JObject prereq, out IList<string> validationErrors) => prereq.IsValid(PrereqSchema, out validationErrors);

    public static bool IsValidPrerequite(string prereqStr, out IList<string> validationErrors)
    {
        var jsonObj = JObject.Parse(prereqStr);
        var name = (string?)jsonObj["name"];

        validationErrors = new List<string>();

        var valid = IsValidPrerequite(jsonObj, out var errors);
        if (!valid)
        {
            Program.Logger.LogError($"Invalid prerequite [{name}].");

            var sb = new StringBuilder();
            sb.AppendLine($"Prerequite [{name}] contains validation errors.");
            sb.Append(errors);

            validationErrors.Add(sb.ToString());
        }

        return valid;
    }
}
