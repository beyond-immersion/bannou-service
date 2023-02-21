using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Services.Data
{
    /// <summary>
    /// The interface for a template definition data model.
    /// 
    /// These are stored with the Template Service, acting
    /// as the starting points / definitions for in-game
    /// object types. This can include items, skills, 
    /// spells, etc...
    /// 
    /// See: <see cref="Template"/> and <see cref="TemplateService"/>
    /// </summary>
    public interface ITemplate
    {
        string ID { get; }
        string Name { get; }
        string? Type { get; }
        string? Description { get; }
        List<string>? Tags { get; }
        List<TemplateContext>? Contexts { get; }

        [JsonIgnore]
        string Slug
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name))
                    return Name;

                return Name.GenerateSlug();
            }
        }
    }
}
