using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// Interface to implement for all internal dapr service,
    /// which provides the logic for any given set of APIs.
    /// 
    /// For example, the Inventory service is in charge of
    /// any API calls that desire to create/modify inventory
    /// data in the game.
    /// </summary>
    public interface IDaprService
    {
        public void Shutdown() { }
    }
}
