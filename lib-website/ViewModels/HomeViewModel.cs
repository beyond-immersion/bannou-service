using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BeyondImmersion.BannouService.Website.Messages;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Website.ViewModels;

/// <summary>
/// View model for the home page using MVVM Community Toolkit.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly IWebsiteService _websiteService;
    private readonly ILogger<HomeViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Checking server status...";

    [ObservableProperty]
    private ServerStatusResponse? _serverStatus;

    [ObservableProperty]
    private List<NewsItem> _newsItems = new();

    [ObservableProperty]
    private bool _hasNews;

    [ObservableProperty]
    private string? _maintenanceMessage;

    public HomeViewModel(IWebsiteService websiteService, ILogger<HomeViewModel> logger)
    {
        _websiteService = websiteService;
        _logger = logger;
    }

    /// <summary>
    /// Loads initial data for the home page.
    /// </summary>
    [RelayCommand]
    public async Task LoadDataAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading server status...";

            // Load server status
            var statusTask = _websiteService.GetServerStatusAsync();
            var newsTask = _websiteService.GetNewsAsync(5, 0);
            var siteStatusTask = _websiteService.GetStatusAsync();

            await Task.WhenAll(statusTask, newsTask, siteStatusTask);

            ServerStatus = await statusTask;
            var newsResponse = await newsTask;
            var siteStatus = await siteStatusTask;

            NewsItems = newsResponse.Items.ToList();
            HasNews = NewsItems.Any();
            MaintenanceMessage = siteStatus.MaintenanceMessage;

            StatusMessage = DetermineStatusMessage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load home page data");
            StatusMessage = "Unable to connect to servers. Please try again later.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the server status.
    /// </summary>
    [RelayCommand]
    public async Task RefreshStatusAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            ServerStatus = await _websiteService.GetServerStatusAsync();
            StatusMessage = DetermineStatusMessage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh server status");
            StatusMessage = "Failed to refresh status";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Navigates to the downloads page.
    /// </summary>
    [RelayCommand]
    public void NavigateToDownloads()
    {
        // Navigation handled by Blazor routing
        _logger.LogInformation("Navigating to downloads");
    }

    /// <summary>
    /// Navigates to the registration page.
    /// </summary>
    [RelayCommand]
    public void NavigateToRegister()
    {
        // Navigation handled by Blazor routing
        _logger.LogInformation("Navigating to registration");
    }

    private string DetermineStatusMessage()
    {
        if (MaintenanceMessage != null)
            return MaintenanceMessage;

        if (ServerStatus == null)
            return "Server status unavailable";

        return ServerStatus.GlobalStatus switch
        {
            "online" => "All servers online",
            "partial" => "Some servers experiencing issues",
            "offline" => "Servers are currently offline",
            "maintenance" => "Scheduled maintenance in progress",
            _ => "Unknown server status"
        };
    }
}