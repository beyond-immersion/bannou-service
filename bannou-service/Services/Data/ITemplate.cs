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
    /// </summary>
    public interface ITemplate
    {
        public interface IContextRef
        {
            string Type { get; }
            string ID { get; }
        }
        string ID { get; }
        string Name { get; }
        string? Type { get; }
        string? Description { get; }
        IEnumerable<string>? Tags { get; }
        IEnumerable<IContextRef>? Contexts { get; }
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
