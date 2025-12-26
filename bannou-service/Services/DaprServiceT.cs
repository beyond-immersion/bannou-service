using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Optional generic base type for service handlers.
/// Automatically initializes the proper configuration.
/// </summary>
[Obsolete]
public abstract class DaprService<T> : DaprService
    where T : class, IServiceConfiguration, new()
{
    private T? _configuration;
    /// <inheritdoc/>
    public T Configuration
    {
        get
        {
            _configuration ??= IServiceConfiguration.BuildConfiguration<T>();
            return _configuration;
        }

        internal set => _configuration = value;
    }
}
