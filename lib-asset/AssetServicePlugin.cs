using BeyondImmersion.BannouService.Asset.Bundles;
using BeyondImmersion.BannouService.Asset.Events;
using BeyondImmersion.BannouService.Asset.Metrics;
using BeyondImmersion.BannouService.Asset.Processing;
using BeyondImmersion.BannouService.Asset.Storage;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;

namespace BeyondImmersion.BannouService.Asset;

/// <summary>
/// Plugin wrapper for Asset service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class AssetServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "asset";
    public override string DisplayName => "Asset Service";

    private IAssetService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [DaprService] attributes
        // No need to register IAssetService and AssetService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register AssetServiceConfiguration here

        // Register MinIO configuration options from AssetServiceConfiguration (Tenet 21)
        services.AddOptions<MinioStorageOptions>()
            .Configure<AssetServiceConfiguration>((options, config) =>
            {
                // Map from AssetServiceConfiguration to MinioStorageOptions
                options.Endpoint = config.StorageEndpoint;
                options.AccessKey = config.StorageAccessKey;
                options.SecretKey = config.StorageSecretKey;
                options.UseSSL = config.StorageUseSsl;
                options.DefaultBucket = config.StorageBucket;
                options.Region = config.StorageRegion;
            });

        // Register IMinioClient using the configured options
        services.AddSingleton<IMinioClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MinioStorageOptions>>().Value;
            var logger = sp.GetService<ILogger<MinioStorageProvider>>();

            // Validate credentials are configured (Tenet 21 - fail-fast for required config)
            if (string.IsNullOrEmpty(options.AccessKey) || string.IsNullOrEmpty(options.SecretKey))
            {
                var message = "MinIO credentials not configured. " +
                    "Set ASSET_STORAGE_ACCESS_KEY and ASSET_STORAGE_SECRET_KEY environment variables.";
                logger?.LogError(message);
                throw new InvalidOperationException(message);
            }

            logger?.LogInformation("Configuring MinIO client: Endpoint={Endpoint}, UseSSL={UseSSL}, Bucket={Bucket}",
                options.Endpoint, options.UseSSL, options.DefaultBucket);

            var client = new MinioClient()
                .WithEndpoint(options.Endpoint)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .WithRegion(options.Region);

            if (options.UseSSL)
            {
                client = client.WithSSL();
            }

            return client.Build();
        });

        // Register storage provider
        services.AddSingleton<IAssetStorageProvider, MinioStorageProvider>();

        // Register event emitter for client notifications
        services.AddScoped<IAssetEventEmitter, AssetEventEmitter>();

        // Register bundle services
        services.AddSingleton<BundleConverter>();
        services.AddSingleton<BundleValidator>();

        // Register metrics
        services.AddSingleton<AssetMetrics>();

        // Register asset processors
        services.AddSingleton<IAssetProcessor, TextureProcessor>();
        services.AddSingleton<IAssetProcessor, ModelProcessor>();
        services.AddSingleton<IAssetProcessor, AudioProcessor>();
        services.AddSingleton<AssetProcessorRegistry>();

        // Register processing worker as hosted service based on processing mode
        // Tenet 21 Exception: Read environment variable directly during ConfigureServices because
        // configuration binding (AssetServiceConfiguration) isn't available until service provider is built.
        // Uses canonical ASSET_PROCESSING_MODE variable name defined in asset-configuration.yaml.
        var processingMode = Environment.GetEnvironmentVariable("ASSET_PROCESSING_MODE") ?? "both";

        if (processingMode.Equals("worker", StringComparison.OrdinalIgnoreCase) ||
            processingMode.Equals("both", StringComparison.OrdinalIgnoreCase))
        {
            Logger?.LogInformation("Registering AssetProcessingWorker (mode: {Mode})", processingMode);
            services.AddHostedService<AssetProcessingWorker>();
        }

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring Asset service application pipeline");

        // The generated AssetController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("Asset service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting Asset service");

        try
        {
            // Wait for MinIO connectivity before resolving services that depend on it
            if (!await WaitForMinioConnectivityAsync(maxRetries: 30, retryDelayMs: 2000))
            {
                Logger?.LogError("MinIO connectivity check failed - Asset service cannot start");
                return false;
            }

            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<IAssetService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IAssetService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnStartAsync for Asset service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Asset service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start Asset service");
            return false;
        }
    }

    /// <summary>
    /// Wait for MinIO storage to be available before starting services that depend on it.
    /// Uses retry with exponential backoff similar to Dapr connectivity checks.
    /// </summary>
    /// <param name="maxRetries">Maximum number of connection attempts.</param>
    /// <param name="retryDelayMs">Base delay between retries in milliseconds.</param>
    /// <returns>True if MinIO is reachable, false if all retries exhausted.</returns>
    private async Task<bool> WaitForMinioConnectivityAsync(int maxRetries = 30, int retryDelayMs = 2000)
    {
        if (_serviceProvider == null)
        {
            Logger?.LogError("ServiceProvider not available for MinIO connectivity check");
            return false;
        }

        var options = _serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<MinioStorageOptions>>()?.Value;
        if (options == null)
        {
            Logger?.LogWarning("MinIO options not configured - skipping connectivity check");
            return true; // Allow startup without MinIO if not configured
        }

        // Skip connectivity check if credentials are not configured
        if (string.IsNullOrEmpty(options.AccessKey) || string.IsNullOrEmpty(options.SecretKey))
        {
            Logger?.LogWarning("MinIO credentials not configured - skipping connectivity check");
            return true; // Allow startup to continue; MinioClient registration will fail with clear error
        }

        Logger?.LogInformation(
            "Waiting for MinIO connectivity at {Endpoint} (max {MaxRetries} attempts, {Delay}ms between retries)",
            options.Endpoint, maxRetries, retryDelayMs);

        // Build a temporary client for connectivity testing
        var testClient = new MinioClient()
            .WithEndpoint(options.Endpoint)
            .WithCredentials(options.AccessKey, options.SecretKey)
            .WithRegion(options.Region);

        if (options.UseSSL)
        {
            testClient = testClient.WithSSL();
        }

        var client = testClient.Build();

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Try to check if the default bucket exists (or list buckets as a health check)
                var bucketExists = await client.BucketExistsAsync(
                    new Minio.DataModel.Args.BucketExistsArgs().WithBucket(options.DefaultBucket));

                if (!bucketExists)
                {
                    // Bucket doesn't exist yet, but MinIO is reachable - create it
                    Logger?.LogInformation("MinIO reachable, creating default bucket: {Bucket}", options.DefaultBucket);
                    await client.MakeBucketAsync(
                        new Minio.DataModel.Args.MakeBucketArgs().WithBucket(options.DefaultBucket));
                }

                Logger?.LogInformation("✅ MinIO connectivity confirmed on attempt {Attempt}", attempt);
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(
                    "MinIO connectivity check failed on attempt {Attempt}/{MaxRetries}: {Message}",
                    attempt, maxRetries, ex.Message);

                if (attempt < maxRetries)
                {
                    var delay = retryDelayMs * Math.Min(attempt, 5); // Cap exponential growth
                    await Task.Delay(delay);
                }
            }
        }

        Logger?.LogError("MinIO connectivity check failed after {MaxRetries} attempts", maxRetries);
        return false;
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("Asset service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnRunningAsync for Asset service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Asset service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down Asset service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnShutdownAsync for Asset service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("Asset service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Asset service shutdown");
        }
    }
}
