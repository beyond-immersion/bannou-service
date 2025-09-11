using BeyondImmersion.BannouService.Website.Messages;

namespace BeyondImmersion.BannouService.Website;

/// <summary>
/// Interface for the website service providing public-facing web portal functionality.
/// </summary>
public interface IWebsiteService
{
    /// <summary>
    /// Gets the current status of the website service.
    /// </summary>
    Task<StatusResponse> GetStatusAsync();

    /// <summary>
    /// Gets cached content for a specific page.
    /// </summary>
    Task<PageContent?> GetPageContentAsync(string page);

    /// <summary>
    /// Gets the latest news and announcements.
    /// </summary>
    Task<NewsResponse> GetNewsAsync(int limit = 10, int offset = 0);

    /// <summary>
    /// Gets the status of all game servers.
    /// </summary>
    Task<ServerStatusResponse> GetServerStatusAsync();

    /// <summary>
    /// Gets download links for game clients.
    /// </summary>
    Task<DownloadsResponse> GetDownloadsAsync(string? platform = null);

    /// <summary>
    /// Submits a contact form.
    /// </summary>
    Task<ContactResponse> SubmitContactAsync(ContactRequest request);

    /// <summary>
    /// Gets the account profile for the authenticated user.
    /// </summary>
    Task<AccountProfile?> GetAccountProfileAsync(string accountId);

    /// <summary>
    /// Gets the character list for the authenticated user.
    /// </summary>
    Task<CharacterListResponse> GetAccountCharactersAsync(string accountId);

    /// <summary>
    /// Gets the subscription status for the authenticated user.
    /// </summary>
    Task<SubscriptionResponse> GetSubscriptionAsync(string accountId);
}