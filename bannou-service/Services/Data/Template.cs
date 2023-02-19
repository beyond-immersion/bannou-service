using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Services.Data
{
    /// <summary>
    /// Implementation of template data model.
    /// See also: <seealso cref="ITemplate"/> and <seealso cref="TemplateService"/>
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class Template : ITemplate
    {
        /// <summary>
        /// 
        /// </summary>
        [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
        public sealed class ContextRef : ITemplate.IContextRef
        {
            /// <summary>
            /// 
            /// </summary>
            [JsonProperty("type", Required = Required.Always)]
            public string Type { get; }

            /// <summary>
            /// 
            /// </summary>
            [JsonProperty("id", Required = Required.Always)]
            public string ID { get; }

            private ContextRef() { }
            public ContextRef(string type, string id)
            {
                if (string.IsNullOrWhiteSpace(type))
                    throw new ArgumentNullException(nameof(type));

                if (string.IsNullOrWhiteSpace(id))
                    throw new ArgumentNullException(nameof(id));

                this.Type = type;
                this.ID = id;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        [JsonProperty("id", Required = Required.Always)]
        public string ID { get; }

        /// <summary>
        /// 
        /// </summary>
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; }

        /// <summary>
        /// 
        /// </summary>
        [JsonProperty("type")]
        public string? Type { get; }

        /// <summary>
        /// 
        /// </summary>
        [JsonProperty("description")]
        public string? Description { get; }

        /// <summary>
        /// 
        /// </summary>
        [JsonProperty("tags")]
        public List<string>? Tags { get; }

        /// <summary>
        /// 
        /// </summary>
        [JsonProperty("contexts")]
        public List<ContextRef>? Contexts { get; }

        IEnumerable<string>? ITemplate.Tags => Tags;
        IEnumerable<ITemplate.IContextRef>? ITemplate.Contexts => Contexts;

        private Template() { }

        public Template(string name, string? action = null, string? description = null, List<string>? tags = null, List<ContextRef>? contexts = null)
            : this(Guid.NewGuid().ToString().ToLower(), name, action, description, tags, contexts) { }

        public Template(string id, string name, string? action = null, string? description = null, List<string>? tags = null, List<ContextRef>? contexts = null)
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
