using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Website.Messages;
using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Website;

/// <summary>
/// Website service implementation with Dapr state management for caching.
/// </summary>
[DaprService("website", typeof(IWebsiteService), lifetime: ServiceLifetime.Singleton)]
public class WebsiteService : DaprService<WebsiteServiceConfiguration>, IWebsiteService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<WebsiteService> _logger;
    private DateTime _startTime;

    public WebsiteService(DaprClient daprClient, ILogger<WebsiteService> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
        _startTime = DateTime.UtcNow;
    }

    public async Task<StatusResponse> GetStatusAsync()
    {
        var uptime = (int)(DateTime.UtcNow - _startTime).TotalSeconds;
        
        // Check if we're in maintenance mode
        var maintenanceMessage = await GetCachedValueAsync<string>("maintenance_message");
        
        return new StatusResponse
        {
            Status = maintenanceMessage != null ? "maintenance" : "healthy",
            Version = "1.0.0",
            Uptime = uptime,
            MaintenanceMessage = maintenanceMessage
        };
    }

    public async Task<PageContent?> GetPageContentAsync(string slug)
    {
        // All content comes from Redis - this is a generic CMS
        var pageKey = $"cms:pages:{slug}";
        
        var content = await _daprClient.GetStateAsync<PageContent>(Configuration.Redis_State_Store, pageKey);
        
        if (content == null)
        {
            _logger.LogWarning("Page not found: {Slug}", slug);
            return null;
        }

        // Only return published pages to non-authenticated users
        if (!content.Published)
        {
            _logger.LogDebug("Page {Slug} is unpublished", slug);
            return null;
        }

        return content;
    }

    public async Task<PageContent> CreatePageAsync(PageContent page)
    {
        // Ensure slug is unique
        var existing = await GetPageContentAsync(page.Slug);
        if (existing != null)
        {
            throw new InvalidOperationException($"Page with slug '{page.Slug}' already exists");
        }

        page.LastModified = DateTime.UtcNow;
        if (page.Published && !page.PublishedAt.HasValue)
        {
            page.PublishedAt = DateTime.UtcNow;
        }

        var pageKey = $"cms:pages:{page.Slug}";
        await _daprClient.SaveStateAsync(Configuration.Redis_State_Store, pageKey, page);

        // Update page index
        await UpdatePageIndexAsync(page.Slug, true);

        _logger.LogInformation("Created page: {Slug}", page.Slug);
        return page;
    }

    public async Task<PageContent> UpdatePageAsync(string slug, PageContent page)
    {
        var pageKey = $"cms:pages:{slug}";
        
        // If slug is changing, ensure new slug is unique
        if (slug != page.Slug)
        {
            var existing = await GetPageContentAsync(page.Slug);
            if (existing != null)
            {
                throw new InvalidOperationException($"Page with slug '{page.Slug}' already exists");
            }
            
            // Delete old key
            await _daprClient.DeleteStateAsync(Configuration.Redis_State_Store, pageKey);
            await UpdatePageIndexAsync(slug, false);
        }

        page.LastModified = DateTime.UtcNow;
        if (page.Published && !page.PublishedAt.HasValue)
        {
            page.PublishedAt = DateTime.UtcNow;
        }

        var newPageKey = $"cms:pages:{page.Slug}";
        await _daprClient.SaveStateAsync(Configuration.Redis_State_Store, newPageKey, page);
        await UpdatePageIndexAsync(page.Slug, true);

        _logger.LogInformation("Updated page: {Slug}", page.Slug);
        return page;
    }

    private async Task UpdatePageIndexAsync(string slug, bool add)
    {
        var indexKey = "cms:pages:index";
        var index = await _daprClient.GetStateAsync<List<string>>(Configuration.Redis_State_Store, indexKey) 
            ?? new List<string>();

        if (add && !index.Contains(slug))
        {
            index.Add(slug);
        }
        else if (!add)
        {
            index.Remove(slug);
        }

        await _daprClient.SaveStateAsync(Configuration.Redis_State_Store, indexKey, index);
    }

    public async Task<NewsResponse> GetNewsAsync(int limit = 10, int offset = 0)
    {
        var cacheKey = $"news_{limit}_{offset}";
        
        var cached = await GetCachedValueAsync<NewsResponse>(cacheKey);
        if (cached != null)
            return cached;

        // In production, load from database
        var news = new NewsResponse
        {
            Items = GenerateSampleNews(limit, offset),
            Total = 50, // Mock total
            HasMore = offset + limit < 50
        };

        await SetCachedValueAsync(cacheKey, news, TimeSpan.FromMinutes(1));
        return news;
    }

    public async Task<ServerStatusResponse> GetServerStatusAsync()
    {
        // This would typically call game server monitoring services
        var cacheKey = "server_status";
        
        var cached = await GetCachedValueAsync<ServerStatusResponse>(cacheKey);
        if (cached != null)
            return cached;

        var status = new ServerStatusResponse
        {
            GlobalStatus = "online",
            Realms = new[]
            {
                new RealmStatus
                {
                    RealmId = "omega",
                    Name = "Omega",
                    Status = "online",
                    Population = "medium",
                    PlayerCount = 1250,
                    Ping = 25
                },
                new RealmStatus
                {
                    RealmId = "arcadia",
                    Name = "Arcadia",
                    Status = "online",
                    Population = "high",
                    PlayerCount = 3500,
                    Ping = 30
                },
                new RealmStatus
                {
                    RealmId = "fantasia",
                    Name = "Fantasia",
                    Status = "maintenance",
                    Population = "low",
                    PlayerCount = null,
                    Ping = null
                }
            }
        };

        await SetCachedValueAsync(cacheKey, status, TimeSpan.FromSeconds(30));
        return status;
    }

    public async Task<DownloadsResponse> GetDownloadsAsync(string? platform = null)
    {
        var cacheKey = $"downloads_{platform ?? "all"}";
        
        var cached = await GetCachedValueAsync<DownloadsResponse>(cacheKey);
        if (cached != null)
            return cached;

        var downloads = new DownloadsResponse
        {
            Clients = GenerateDownloadLinks(platform)
        };

        await SetCachedValueAsync(cacheKey, downloads, TimeSpan.FromHours(1));
        return downloads;
    }

    public async Task<ContactResponse> SubmitContactAsync(ContactRequest request)
    {
        // Check rate limiting
        var rateLimitKey = $"contact_rate_{request.Email}";
        var submissions = await GetCachedValueAsync<int>(rateLimitKey);
        
        if (submissions >= Configuration.Contact_Rate_Limit)
        {
            throw new InvalidOperationException("Too many contact form submissions. Please try again later.");
        }

        // In production, send email or create support ticket
        _logger.LogInformation("Contact form submitted from {Email} with subject: {Subject}", 
            request.Email, request.Subject);

        // Update rate limit counter
        await SetCachedValueAsync(rateLimitKey, submissions + 1, TimeSpan.FromHours(1));

        var ticketId = Guid.NewGuid();
        
        // Store the contact request
        await SetCachedValueAsync($"contact_{ticketId}", request, TimeSpan.FromDays(7));

        return new ContactResponse
        {
            TicketId = ticketId,
            Message = "Thank you for contacting us. We will respond within 24-48 hours."
        };
    }

    public async Task<AccountProfile?> GetAccountProfileAsync(string accountId)
    {
        // This would call the accounts service via Dapr
        try
        {
            var response = await _daprClient.InvokeMethodAsync<object, AccountProfile>(
                "accounts",
                $"accounts/{accountId}",
                new { });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get account profile for {AccountId}", accountId);
            return null;
        }
    }

    public async Task<CharacterListResponse> GetAccountCharactersAsync(string accountId)
    {
        // This would call the character service via Dapr
        try
        {
            var response = await _daprClient.InvokeMethodAsync<object, CharacterListResponse>(
                "characters",
                $"characters/account/{accountId}",
                new { });
            return response;
        }
        catch
        {
            // Return empty if service not available
            return new CharacterListResponse { Characters = Array.Empty<CharacterSummary>() };
        }
    }

    public async Task<SubscriptionResponse> GetSubscriptionAsync(string accountId)
    {
        // This would check subscription status
        var cacheKey = $"subscription_{accountId}";
        
        var cached = await GetCachedValueAsync<SubscriptionResponse>(cacheKey);
        if (cached != null)
            return cached;

        var subscription = new SubscriptionResponse
        {
            Status = "active",
            Type = "premium",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            AutoRenew = true,
            Benefits = new[] { "Extra character slots", "Priority queue", "Exclusive cosmetics" }
        };

        await SetCachedValueAsync(cacheKey, subscription, TimeSpan.FromMinutes(5));
        return subscription;
    }

    #region Cache Helpers

    private async Task<T?> GetCachedValueAsync<T>(string key) where T : class
    {
        try
        {
            var cached = await _daprClient.GetStateAsync<string>(Configuration.Redis_State_Store, key);
            if (string.IsNullOrEmpty(cached))
                return null;

            return JsonSerializer.Deserialize<T>(cached);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cached value for key {Key}", key);
            return null;
        }
    }

    private async Task SetCachedValueAsync<T>(string key, T value, TimeSpan expiration)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            var metadata = new Dictionary<string, string>
            {
                { "ttlInSeconds", ((int)expiration.TotalSeconds).ToString() }
            };

            await _daprClient.SaveStateAsync(Configuration.Redis_State_Store, key, json, metadata: metadata);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache value for key {Key}", key);
        }
    }

    #endregion

    #region Sample Data Generation

    private PageContent? GenerateDefaultPageContent(string page)
    {
        return page switch
        {
            "home" => new PageContent
            {
                Page = "home",
                Title = "Welcome to Arcadia",
                Content = "# Welcome to Arcadia\n\nExperience a revolutionary MMORPG...",
                LastModified = DateTime.UtcNow
            },
            "about" => new PageContent
            {
                Page = "about",
                Title = "About Arcadia",
                Content = "# About Arcadia\n\nArcadia is a layered meta-reality MMORPG...",
                LastModified = DateTime.UtcNow
            },
            _ => null
        };
    }

    private NewsItem[] GenerateSampleNews(int limit, int offset)
    {
        var items = new List<NewsItem>();
        for (int i = offset; i < offset + limit && i < 50; i++)
        {
            items.Add(new NewsItem
            {
                Id = Guid.NewGuid(),
                Title = $"News Item {i + 1}",
                Summary = $"Summary for news item {i + 1}",
                Content = $"Full content for news item {i + 1}...",
                Author = "Arcadia Team",
                PublishedAt = DateTime.UtcNow.AddDays(-i),
                Tags = new[] { "update", "announcement" }
            });
        }
        return items.ToArray();
    }

    private DownloadInfo[] GenerateDownloadLinks(string? platform)
    {
        var downloads = new List<DownloadInfo>();
        
        if (platform == null || platform == "windows")
        {
            downloads.Add(new DownloadInfo
            {
                Platform = "windows",
                Version = "1.0.0",
                Url = "https://download.arcadia.com/windows/ArcadiaSetup.exe",
                Size = 5_000_000_000, // 5GB
                Checksum = "abc123...",
                MinimumRequirements = new Dictionary<string, object>
                {
                    { "os", "Windows 10 64-bit" },
                    { "processor", "Intel i5 or AMD equivalent" },
                    { "memory", "8GB RAM" },
                    { "graphics", "NVIDIA GTX 1060 or AMD equivalent" },
                    { "storage", "50GB available space" }
                }
            });
        }
        
        // Add other platforms...
        
        return downloads.ToArray();
    }

    #endregion
}