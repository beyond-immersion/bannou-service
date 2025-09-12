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
    /// Get latest news and announcements
    /// </summary>
    /// <param name="limit">Number of news items to return</param>
    /// <param name="offset">Pagination offset</param>
    /// <returns>News items retrieved</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<NewsResponse>> GetNews(int? limit = 10, int? offset = 0, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Get game server status for all realms
    /// </summary>
    /// <returns>Server status information</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<ServerStatusResponse>> GetServerStatus(CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Get download links for game clients
    /// </summary>
    /// <param name="platform">Filter by platform</param>
    /// <returns>Download links retrieved</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<DownloadsResponse>> GetDownloads(Platform? platform = null, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Submit contact form
    /// </summary>
    /// <returns>Contact form submitted</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<ContactResponse>> SubmitContact(ContactRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Get account profile for logged-in user
    /// </summary>
    /// <returns>Account profile retrieved</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<AccountProfile>> GetAccountProfile(CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Get character list for logged-in user
    /// </summary>
    /// <returns>Character list retrieved</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<CharacterListResponse>> GetAccountCharacters(CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// List all CMS pages
    /// </summary>
    /// <returns>Page list retrieved</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Collections.Generic.ICollection<PageMetadata>>> ListPages(bool? includeUnpublished = false, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Create new CMS page
    /// </summary>
    /// <returns>Page created</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<PageContent>> CreatePage(PageContent body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UpdatePage operation
    /// </summary>
    Task<(StatusCodes, PageContent?)> UpdatePageAsync(string slug, PageContent body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetSiteSettings operation
    /// </summary>
    Task<(StatusCodes, SiteSettings?)> GetSiteSettingsAsync(CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Update site configuration
    /// </summary>
    /// <returns>Settings updated</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<SiteSettings>> UpdateSiteSettings(SiteSettings body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Get current theme configuration
    /// </summary>
    /// <returns>Theme configuration retrieved</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<ThemeConfig>> GetTheme(CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Update theme configuration
    /// </summary>
    /// <returns>Theme updated</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> UpdateTheme(ThemeConfig body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Get subscription status
    /// </summary>
    /// <returns>Subscription information retrieved</returns>
    public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<SubscriptionResponse>> GetSubscription(CancellationToken cancellationToken = default(CancellationToken));

}
