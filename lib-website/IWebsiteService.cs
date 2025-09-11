using Microsoft.AspNetCore.Mvc;
using BeyondImmersion.BannouService.Controllers.Generated;

namespace BeyondImmersion.BannouService.Website;

/// <summary>
/// Interface for website service operations.
/// Implements business logic for the generated WebsiteController methods.
/// </summary>
public interface IWebsiteService
{
    /// <summary>
    /// Get website status and version
    /// </summary>
    Task<ActionResult<StatusResponse>> GetStatusAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get dynamic page content from CMS
    /// </summary>
    Task<ActionResult<PageContent>> GetPageContentAsync(
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get latest news and announcements
    /// </summary>
    Task<ActionResult<NewsResponse>> GetNewsAsync(
        int? limit = 10,
        int? offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get game server status and metrics
    /// </summary>
    Task<ActionResult<ServerStatusResponse>> GetServerStatusAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get available downloads by platform
    /// </summary>
    Task<ActionResult<DownloadsResponse>> GetDownloadsAsync(
        Platform? platform,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submit contact form message
    /// </summary>
    Task<ActionResult<ContactResponse>> SubmitContactAsync(
        ContactRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get authenticated user's account profile
    /// </summary>
    Task<ActionResult<AccountProfile>> GetAccountProfileAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get authenticated user's character list
    /// </summary>
    Task<ActionResult<CharacterListResponse>> GetAccountCharactersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List all CMS pages with metadata
    /// </summary>
    Task<ActionResult<System.Collections.Generic.ICollection<PageMetadata>>> ListPagesAsync(
        bool? includeUnpublished = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create new CMS page
    /// </summary>
    Task<ActionResult<PageContent>> CreatePageAsync(
        PageContent body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update existing CMS page
    /// </summary>
    Task<ActionResult<PageContent>> UpdatePageAsync(
        string slug,
        PageContent body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete CMS page
    /// </summary>
    Task<IActionResult> DeletePageAsync(
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get site-wide configuration settings
    /// </summary>
    Task<ActionResult<SiteSettings>> GetSiteSettingsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update site-wide configuration settings
    /// </summary>
    Task<ActionResult<SiteSettings>> UpdateSiteSettingsAsync(
        SiteSettings body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current theme configuration
    /// </summary>
    Task<ActionResult<ThemeConfig>> GetThemeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update theme configuration
    /// </summary>
    Task<IActionResult> UpdateThemeAsync(
        ThemeConfig body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user subscription status
    /// </summary>
    Task<ActionResult<SubscriptionResponse>> GetSubscriptionAsync(
        CancellationToken cancellationToken = default);
}