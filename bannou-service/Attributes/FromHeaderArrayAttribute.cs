using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute to propagate common headers through dapr requests.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FromHeaderArrayAttribute : Attribute, IBindingSourceMetadata, IModelNameProvider, IFromHeaderMetadata
{
    public BindingSource BindingSource => BindingSource.Header;
    public string? Name { get; set; }
    public string? Delimeter { get; set; }
}
