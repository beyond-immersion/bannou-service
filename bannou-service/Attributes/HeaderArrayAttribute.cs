using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute to propagate common headers through dapr requests.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class HeaderArrayAttribute : Attribute, IBindingSourceMetadata, IModelNameProvider, IFromHeaderMetadata, IBinderTypeProviderMetadata
{
    public BindingSource BindingSource => BindingSource.Header;
    public Type? BinderType => typeof(HeaderArrayModelBinder);
    public string? Name { get; set; }
    public string? Delimeter { get; set; }
}
