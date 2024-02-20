using BeyondImmersion.BannouService.Behaviour.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Authorization;
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

    public static bool ResolveBehaviourRefs(JObject behaviour, Dictionary<string, JObject> refLookup)
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

    public static bool IsValidBehaviour(JObject jsonObj, out IList<string> validationErrors) => jsonObj.IsValid(BehaviourSchema, out validationErrors);

    public static bool IsValidBehaviour(string jsonStr, out IList<string> validationErrors)
    {
        var jsonObj = JObject.Parse(jsonStr);

        return IsValidBehaviour(jsonObj, out validationErrors);
    }

    public static bool IsValidPrerequite(JObject jsonObj, out IList<string> validationErrors) => jsonObj.IsValid(PrereqSchema, out validationErrors);

    public static bool IsValidPrerequite(string jsonStr, out IList<string> validationErrors)
    {
        var jsonObj = JObject.Parse(jsonStr);

        return IsValidPrerequite(jsonObj, out validationErrors);
    }
}
