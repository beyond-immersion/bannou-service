using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Services.Data
{
    /// <summary>
    /// A reference to a given "template context".
    /// Template contexts contain the bulk of the data
    /// describing the abilities / usage of a given
    /// object in a way that game services can act on.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class TemplateContextRef
    {
        /// <summary>
        /// The context type- examples might be:
        ///     "consumable_effect"
        ///     "armor_effect"
        ///     "weapon_effect"
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

        private TemplateContextRef() { }
        public TemplateContextRef(string type, string id)
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
        /// The unique identifier of this template (GUID, slug, etc).
        /// </summary>
        [JsonProperty("id", Required = Required.Always)]
        public string ID { get; }

        /// <summary>
        /// The (human-readable) name of this template.
        /// </summary>
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; }

        /// <summary>
        /// The type of template. Can simply be the name
        /// of the service handling these template objects.
        ///
        /// Examples: item, magic, skill,
        ///     item_svc, combat_svc, etc...
        /// </summary>
        [JsonProperty("type", Required = Required.Always)]
        public string Type { get; }

        /// <summary>
        /// An optional description of what this item
        /// does / how it should be used.
        /// </summary>
        [JsonProperty("description")]
        public string? Description { get; }

        /// <summary>
        /// Optional tags describing this template object.
        /// Used in /list to generate sets from shared tags.
        /// 
        /// Examples:
        ///     (for gear type) armor, weapon, accessory
        ///     (for item type) consumable, material
        ///     (for skill type) active, passive
        /// </summary>
        [JsonProperty("tags")]
        public List<string>? Tags { get; }

        /// <summary>
        /// The list of contexts that this template possesses.
        /// The final "item" is built from compiling these contexts
        /// together with the metadata here.
        /// </summary>
        [JsonProperty("contexts")]
        public List<TemplateContextRef>? Contexts { get; }

        private Template() { }
        public Template(string id, string name, string type, string? description = null, List<string>? tags = null, List<TemplateContextRef>? contexts = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentNullException(nameof(type));

            if (tags != null && tags.Count == 0)
                tags = null;

            if (contexts != null && contexts.Count == 0)
                contexts = null;

            ID = id;
            Name = name;
            Type = type;
            Description = description;
            Tags = tags;
            Contexts = contexts;
        }
    }
}
