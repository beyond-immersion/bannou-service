using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Services.Data
{
    /// <summary>
    /// A reference to a given "item context".
    /// Item contexts contain the bulk of the data
    /// describing the abilities / usage of a given
    /// item in a way that game services can act on.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class TemplateContext
    {
        /// <summary>
        /// The context type- examples might be:
        ///     "CONSUMABLE"
        ///     "ARMOR"
        ///     "WEAPON"
        ///
        /// ... or they may be more specific.
        /// </summary>
        [JsonProperty("type", Required = Required.Always)]
        public string Type { get; }

        /// <summary>
        /// The GUID of the related context entry.
        /// </summary>
        [JsonProperty("id", Required = Required.Always)]
        public string ID { get; }

        private TemplateContext() { }
        public TemplateContext(string type, string id)
        {
            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentNullException(nameof(type));

            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            Type = type;
            ID = id;
        }
    }

    /// <summary>
    /// Implementation of template data model.
    /// See also: <seealso cref="ITemplate"/> and <seealso cref="TemplateService"/>
    /// 
    /// Only "ID" and "Name" are actually required fields to create a template.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class Template : ITemplate
    {
        /// <summary>
        /// The GUID of this template.
        /// </summary>
        [JsonProperty("id", Required = Required.Always)]
        public string ID { get; }

        /// <summary>
        /// The (human-readable) name of this template.
        /// </summary>
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; }

        /// <summary>
        /// The type of template- optional, and used
        /// purely for generating datasets.
        /// </summary>
        [JsonProperty("type")]
        public string? Type { get; }

        /// <summary>
        /// An optional description of what this item
        /// does / how it should be used.
        /// </summary>
        [JsonProperty("description")]
        public string? Description { get; }

        /// <summary>
        /// Optional tags to include.
        /// Used in lookups/lists to create datasets.
        /// </summary>
        [JsonProperty("tags")]
        public List<string>? Tags { get; }

        /// <summary>
        /// The list of contexts that this template possesses.
        /// The final "item" is built from compiling these contexts
        /// together with the metadata here.
        /// </summary>
        [JsonProperty("contexts")]
        public List<TemplateContext>? Contexts { get; }

        private Template() { }
        public Template(string id, string name, string? action = null, string? description = null, List<string>? tags = null, List<TemplateContext>? contexts = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if (tags != null && tags.Count == 0)
                tags = null;

            if (contexts != null && contexts.Count == 0)
                contexts = null;

            ID = id;
            Name = name;
            Type = action;
            Description = description;
            Tags = tags;
            Contexts = contexts;
        }
    }
}
