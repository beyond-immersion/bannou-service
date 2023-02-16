namespace BeyondImmersion.BannouService.Services
{
    public interface IDaprService
    {
        public string ServiceID { get; }
        public void AddEndpointsToWebApp(WebApplication? webApp);
        public void Shutdown() { }
    }
}
