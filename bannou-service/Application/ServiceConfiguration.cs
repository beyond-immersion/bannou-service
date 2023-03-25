using System.Reflection;
using System.Text.Json;
using Dapr.Extensions.Configuration;

namespace BeyondImmersion.BannouService.Application;

[ServiceConfiguration]
public class ServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Set to override GUID for administrative service endpoints.
    /// If not set, will generate a new GUID automatically on service startup.
    /// </summary>
    public string? ForceServiceID { get; set; }
}
