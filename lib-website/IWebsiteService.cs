using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Website
{
    /// <summary>
    /// Service interface for Website API - generated from controller
    /// </summary>
    public interface IWebsiteService
    {
        /// <summary>
        /// GetStatus operation  
        /// </summary>
        Task<(StatusCodes, StatusResponse?)> GetStatusAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetPageContent operation  
        /// </summary>
        Task<(StatusCodes, PageContent?)> GetPageContentAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetNews operation  
        /// </summary>
        Task<(StatusCodes, NewsResponse?)> GetNewsAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetServerStatus operation  
        /// </summary>
        Task<(StatusCodes, ServerStatusResponse?)> GetServerStatusAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetDownloads operation  
        /// </summary>
        Task<(StatusCodes, DownloadsResponse?)> GetDownloadsAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// SubmitContact operation  
        /// </summary>
        Task<(StatusCodes, ContactResponse?)> SubmitContactAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetAccountProfile operation  
        /// </summary>
        Task<(StatusCodes, AccountProfile?)> GetAccountProfileAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetAccountCharacters operation  
        /// </summary>
        Task<(StatusCodes, CharacterListResponse?)> GetAccountCharactersAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// CreatePage operation  
        /// </summary>
        Task<(StatusCodes, PageContent?)> CreatePageAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// UpdatePage operation  
        /// </summary>
        Task<(StatusCodes, PageContent?)> UpdatePageAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetSiteSettings operation  
        /// </summary>
        Task<(StatusCodes, SiteSettings?)> GetSiteSettingsAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// UpdateSiteSettings operation  
        /// </summary>
        Task<(StatusCodes, SiteSettings?)> UpdateSiteSettingsAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetTheme operation  
        /// </summary>
        Task<(StatusCodes, ThemeConfig?)> GetThemeAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetSubscription operation  
        /// </summary>
        Task<(StatusCodes, SubscriptionResponse?)> GetSubscriptionAsync(/* TODO: Add parameters from schema */);

    }
}
