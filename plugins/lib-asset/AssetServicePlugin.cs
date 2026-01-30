using Amazon.Runtime;
using Amazon.S3;
using BeyondImmersion.Bannou.Bundle.Format;
using BeyondImmersion.BannouService.Asset.Bundles;
using BeyondImmersion.BannouService.Asset.Events;
using BeyondImmersion.BannouService.Asset.Metrics;
using BeyondImmersion.BannouService.Asset.Pool;
using BeyondImmersion.BannouService.Asset.Processing;
using BeyondImmersion.BannouService.Asset.Storage;
using BeyondImmersion.BannouService.Configuration;
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

        // Register MinIO configuration options from AssetServiceConfiguration (IMPLEMENTATION TENETS)
        services.AddOptions<MinioStorageOptions>()
            .Configure<AssetServiceConfiguration, AppConfiguration>((options, config, appConfig) =>
            {
                options.Endpoint = config.StorageEndpoint;

                // Public endpoint for pre-signed URLs: use explicit config, or derive from ServiceDomain
                if (!string.IsNullOrWhiteSpace(config.StoragePublicEndpoint))
                {
                    options.PublicEndpoint = config.StoragePublicEndpoint;
                }
                else
                {
                    var serviceDomain = appConfig.ServiceDomain;
                    if (!string.IsNullOrWhiteSpace(serviceDomain))
                    {
                        // Default: use ServiceDomain with port 9000 for direct MinIO access
                        options.PublicEndpoint = $"{serviceDomain}:9000";
                    }
                    // If neither configured, PublicEndpoint stays null and URLs use internal endpoint
                }

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

        // Register AWS S3 client for presigned URLs (MinIO SDK has bug with Content-Type signing)
        // See: https://github.com/minio/minio-dotnet/issues/1150
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MinioStorageOptions>>().Value;
            var logger = sp.GetService<ILogger<MinioStorageProvider>>();

            var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
            var assetConfig = sp.GetRequiredService<AssetServiceConfiguration>();
            var config = new AmazonS3Config
            {
                ServiceURL = $"{(options.UseSSL ? "https" : "http")}://{options.Endpoint}",
                ForcePathStyle = assetConfig.StorageForcePathStyle,
                UseHttp = !options.UseSSL
            };

            logger?.LogInformation(
                "Configuring AWS S3 client for MinIO presigned URLs: ServiceURL={ServiceURL}",
                config.ServiceURL);

            return new AmazonS3Client(credentials, config);
        });

        // Register storage provider
        services.AddSingleton<IAssetStorageProvider, MinioStorageProvider>();

        // Register event emitter for client notifications
        services.AddScoped<IAssetEventEmitter, AssetEventEmitter>();

        // Register bundle services with configuration-driven cache TTL
        services.AddSingleton<IBundleConverter>(sp =>
        {
            var bundleLogger = sp.GetRequiredService<ILogger<BundleConverter>>();
            var assetConf = sp.GetRequiredService<AssetServiceConfiguration>();
            return new BundleConverter(
                bundleLogger,
                cacheDirectory: null,
                cacheTtl: TimeSpan.FromHours(assetConf.ZipCacheTtlHours));
        });
        services.AddSingleton<BundleValidator>();

        // Register metrics
        services.AddSingleton<AssetMetrics>();

        // Register FFmpeg service for audio/video transcoding
        services.AddSingleton<IFFmpegService, FFmpegService>();

        // Register asset processors
        services.AddSingleton<IAssetProcessor, TextureProcessor>();
        services.AddSingleton<IAssetProcessor, ModelProcessor>();
        services.AddSingleton<IAssetProcessor, AudioProcessor>();
        services.AddSingleton<AssetProcessorRegistry>();

        // Register processor pool manager for tracking processor node state
        services.AddSingleton<IAssetProcessorPoolManager, AssetProcessorPoolManager>();

        // Register background worker for asset processing
        // Worker checks ProcessingMode from configuration at startup and exits early if mode is "api"
        services.AddHostedService<AssetProcessingWorker>();

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

        // MinioClient fluent API: all methods return 'this', Build() returns same instance as IMinioClient
        // Declare disposable before try, dispose unconditionally in finally
        MinioClient? minioClient = null;
        try
        {
            minioClient = new MinioClient();
            minioClient.WithEndpoint(options.Endpoint)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .WithRegion(options.Region);

            if (options.UseSSL)
            {
                minioClient.WithSSL();
            }

            var client = minioClient.Build();

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
        finally
        {
            minioClient?.Dispose();
        }
    }
}
