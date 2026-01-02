using BeyondImmersion.BannouService.Asset.Bundles;
using BeyondImmersion.BannouService.Asset.Events;
using BeyondImmersion.BannouService.Asset.Metrics;
using BeyondImmersion.BannouService.Asset.Processing;
using BeyondImmersion.BannouService.Asset.Storage;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;

namespace BeyondImmersion.BannouService.Asset;

/// <summary>
/// Plugin wrapper for Asset service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class AssetServicePlugin : StandardServicePlugin<IAssetService>
{
    public override string PluginName => "asset";
    public override string DisplayName => "Asset Service";

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Register MinIO configuration options from AssetServiceConfiguration (Tenet 21)
        services.AddOptions<MinioStorageOptions>()
            .Configure<AssetServiceConfiguration>((options, config) =>
            {
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

        // T21 Exception: ConfigureServices bootstrap - reading config before service provider exists
        // Determines whether to register AssetProcessingWorker as a hosted service.
        // Values: "api" (HTTP only), "worker" (processing only), "both" (default)
        var processingMode = Environment.GetEnvironmentVariable("ASSET_PROCESSING_MODE") ?? "both";

        if (processingMode.Equals("worker", StringComparison.OrdinalIgnoreCase) ||
            processingMode.Equals("both", StringComparison.OrdinalIgnoreCase))
        {
            Logger?.LogInformation("Registering AssetProcessingWorker (mode: {Mode})", processingMode);
            services.AddHostedService<AssetProcessingWorker>();
        }

        Logger?.LogDebug("Service dependencies configured");
    }

    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting Asset service");

        // Wait for MinIO connectivity before resolving services that depend on it
        if (!await WaitForMinioConnectivityAsync(maxRetries: 30, retryDelayMs: 2000))
        {
            Logger?.LogError("MinIO connectivity check failed - Asset service cannot start");
            return false;
        }

        // Call base to resolve service and call IBannouService.OnStartAsync
        return await base.OnStartAsync();
    }

    /// <summary>
    /// Wait for MinIO storage to be available before starting services that depend on it.
    /// </summary>
    private async Task<bool> WaitForMinioConnectivityAsync(int maxRetries = 30, int retryDelayMs = 2000)
    {
        if (ServiceProvider == null)
        {
            Logger?.LogError("ServiceProvider not available for MinIO connectivity check");
            return false;
        }

        var options = ServiceProvider.GetService<IOptions<MinioStorageOptions>>()?.Value;
        if (options == null)
        {
            Logger?.LogWarning("MinIO options not configured - skipping connectivity check");
            return true;
        }

        if (string.IsNullOrEmpty(options.AccessKey) || string.IsNullOrEmpty(options.SecretKey))
        {
            Logger?.LogWarning("MinIO credentials not configured - skipping connectivity check");
            return true;
        }

        Logger?.LogInformation(
            "Waiting for MinIO connectivity at {Endpoint} (max {MaxRetries} attempts, {Delay}ms between retries)",
            options.Endpoint, maxRetries, retryDelayMs);

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
                var bucketExists = await client.BucketExistsAsync(
                    new Minio.DataModel.Args.BucketExistsArgs().WithBucket(options.DefaultBucket));

                if (!bucketExists)
                {
                    Logger?.LogInformation("MinIO reachable, creating default bucket: {Bucket}", options.DefaultBucket);
                    await client.MakeBucketAsync(
                        new Minio.DataModel.Args.MakeBucketArgs().WithBucket(options.DefaultBucket));
                }

                Logger?.LogInformation("MinIO connectivity confirmed on attempt {Attempt}", attempt);
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(
                    "MinIO connectivity check failed on attempt {Attempt}/{MaxRetries}: {Message}",
                    attempt, maxRetries, ex.Message);

                if (attempt < maxRetries)
                {
                    var delay = retryDelayMs * Math.Min(attempt, 5);
                    await Task.Delay(delay);
                }
            }
        }

        Logger?.LogError("MinIO connectivity check failed after {MaxRetries} attempts", maxRetries);
        return false;
    }
}
