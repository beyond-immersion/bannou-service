using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Website;

/// <summary>
/// Service interface for Website API - generated from controller
/// </summary>
public interface IWebsiteService
{
        /// <summary>
        /// GetStatus operation
        /// </summary>
        Task<(StatusCodes, StatusResponse?)> GetStatusAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetPageContent operation
        /// </summary>
        Task<(StatusCodes, PageContent?)> GetPageContentAsync(string slug, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetNews operation
        /// </summary>
        Task<(StatusCodes, NewsResponse?)> GetNewsAsync(int? limit = 10, int? offset = 0, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetServerStatus operation
        /// </summary>
        Task<(StatusCodes, ServerStatusResponse?)> GetServerStatusAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetDownloads operation
        /// </summary>
        Task<(StatusCodes, DownloadsResponse?)> GetDownloadsAsync(Platform? platform, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// SubmitContact operation
        /// </summary>
        Task<(StatusCodes, ContactResponse?)> SubmitContactAsync(ContactRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetAccountProfile operation
        /// </summary>
        Task<(StatusCodes, AccountProfile?)> GetAccountProfileAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetAccountCharacters operation
        /// </summary>
        Task<(StatusCodes, CharacterListResponse?)> GetAccountCharactersAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ListPages operation
        /// </summary>
        Task<(StatusCodes, ICollection<PageMetadata>?)> ListPagesAsync(bool? includeUnpublished = false, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// CreatePage operation
        /// </summary>
        Task<(StatusCodes, PageContent?)> CreatePageAsync(PageContent body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdatePage operation
        /// </summary>
        Task<(StatusCodes, PageContent?)> UpdatePageAsync(string slug, PageContent body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// DeletePage operation
        /// </summary>
        Task<(StatusCodes, object?)> DeletePageAsync(string slug, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetSiteSettings operation
        /// </summary>
        Task<(StatusCodes, SiteSettings?)> GetSiteSettingsAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateSiteSettings operation
        /// </summary>
        Task<(StatusCodes, SiteSettings?)> UpdateSiteSettingsAsync(SiteSettings body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetTheme operation
        /// </summary>
        Task<(StatusCodes, ThemeConfig?)> GetThemeAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateTheme operation
        /// </summary>
        Task<(StatusCodes, object?)> UpdateThemeAsync(ThemeConfig body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetSubscription operation
        /// </summary>
        Task<(StatusCodes, SubscriptionResponse?)> GetSubscriptionAsync(CancellationToken cancellationToken = default(CancellationToken));

}
