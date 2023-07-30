using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Models;

/// <summary>
/// The interface for a template definition data model.
/// 
/// These are stored with the Template Service, acting
/// as the starting points / definitions for in-game
/// object types. This can include items, skills, 
/// spells, etc...
/// 
/// See: <see cref="TemplateModel"/> and <see cref="TemplateService"/>
/// </summary>
public interface ITemplateModel
{
    string ID { get; }
    string Name { get; }
    string Type { get; }
    string? Description { get; }
    List<string>? Tags { get; }
    List<TemplateContextRef>? Contexts { get; }

    [JsonIgnore]
    string Slug => string.IsNullOrWhiteSpace(Name) ? Name : Name.GenerateSlug();
}
