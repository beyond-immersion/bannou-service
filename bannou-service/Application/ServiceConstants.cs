using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Application
{
    public static class ServiceConstants
    {
        /// <summary>
        /// Whether internal dapr services are enabled by default.
        /// 
        /// When set to false, docker/ENVs/switches must be set to ENABLE each dapr service individually.
        /// When set to true, docker/ENVs/switches must be used to DISABLE each dapr service individually.
        /// </summary>
        public const bool ENABLE_SERVICES_BY_DEFAULT = false;

        /// <summary>
        /// The placeholder to put into routes, to indicate that it should be replaced with the unique dapr service UUID.
        /// </summary>
        public const string SERVICE_UUID_PLACEHOLDER = "{service_uuid}";

        /// <summary>
        /// Shared serializer options, between all dapr services/consumers.
        /// </summary>
        public static readonly JsonSerializerOptions DAPR_SERIALIZER_OPTIONS = new()
        {
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
            IgnoreReadOnlyFields = false,
            IgnoreReadOnlyProperties = false,
            IncludeFields = false,
            MaxDepth = 32,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnknownTypeHandling = System.Text.Json.Serialization.JsonUnknownTypeHandling.JsonElement,
            WriteIndented = false
        };
    }
}
