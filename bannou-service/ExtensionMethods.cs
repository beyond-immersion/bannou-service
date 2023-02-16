using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService
{
    public static partial class ExtensionMethods
    {
        public static T AddDaprService<T>(this WebApplication? webApp)
            where T : IDaprService, new()
        {
            var newService = new T();
            newService.AddEndpointsToWebApp(webApp);
            return newService;
        }
    }
}
