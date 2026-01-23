using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Asset.Webhooks;

/// <summary>
/// Handles MinIO S3 event notifications for upload completion.
/// MinIO sends webhooks when objects are created in the bucket.
/// </summary>
public class MinioWebhookHandler
{
    private readonly IStateStore<UploadSession> _stateStore;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<MinioWebhookHandler> _logger;
    private readonly AssetServiceConfiguration _configuration;

    // Key prefix now comes from configuration (UploadSessionKeyPrefix)

    public MinioWebhookHandler(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<MinioWebhookHandler> logger,
        AssetServiceConfiguration configuration)
    {
        _configuration = configuration;
        _stateStore = stateStoreFactory.GetStore<UploadSession>(StateStoreDefinitions.Asset);
        _messageBus = messageBus;
        _logger = logger;
    }

    /// <summary>
    /// Handles MinIO S3 event notification webhook.
    /// </summary>
    /// <param name="payload">Raw JSON payload from MinIO webhook</param>
    /// <returns>True if the event was processed successfully</returns>
    public async Task<bool> HandleWebhookAsync(string payload)
    {
        try
        {
            var notification = BannouJson.Deserialize<MinioNotification>(payload);
            if (notification?.Records == null || notification.Records.Count == 0)
            {
                _logger.LogWarning("MinIO webhook: Empty or invalid notification");
                return false;
            }

            foreach (var record in notification.Records)
            {
                await ProcessRecordAsync(record).ConfigureAwait(false);
            }

            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "MinIO webhook: Failed to parse notification payload");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MinIO webhook: Unexpected error processing notification");
            return false;
        }
    }

    private async Task ProcessRecordAsync(MinioEventRecord record)
    {
        var eventName = record.EventName;
        var bucket = record.S3?.Bucket?.Name;
        var key = record.S3?.Object?.Key;
        var etag = record.S3?.Object?.ETag;
        var size = record.S3?.Object?.Size ?? 0;

        _logger.LogDebug("MinIO webhook: Processing event {EventName} for {Bucket}/{Key}",
            eventName, bucket, key);

        // Only process object creation events
        if (eventName?.StartsWith("s3:ObjectCreated:", StringComparison.OrdinalIgnoreCase) != true)
        {
            _logger.LogDebug("MinIO webhook: Ignoring non-creation event {EventName}", eventName);
            return;
        }

        if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(key))
        {
            _logger.LogWarning("MinIO webhook: Missing bucket or key in event");
            return;
        }

        // Check if this is a temp upload (format: {TempUploadPathPrefix}/{uploadId}/{filename})
        var tempPrefix = _configuration.TempUploadPathPrefix.TrimEnd('/') + "/";
        if (!key.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("MinIO webhook: Ignoring non-temp upload {Key}", key);
            return;
        }

        // Extract upload ID from key
        var parts = key.Split('/');
        if (parts.Length < 3)
        {
            _logger.LogWarning("MinIO webhook: Invalid temp key format {Key}", key);
            return;
        }

        var uploadIdStr = parts[1];
        if (!Guid.TryParse(uploadIdStr, out var uploadId))
        {
            _logger.LogWarning("MinIO webhook: Invalid upload ID in key {Key}", key);
            return;
        }

        // Look up the upload session
        var stateKey = $"{_configuration.UploadSessionKeyPrefix}{uploadId}";
        var session = await _stateStore.GetAsync(stateKey).ConfigureAwait(false);

        if (session == null)
        {
            _logger.LogWarning("MinIO webhook: Upload session not found {UploadId}", uploadId);
            return;
        }

        // Update session with upload completion info
        session.UploadedEtag = etag?.Trim('"');
        session.UploadedSize = size;
        session.IsComplete = true;

        await _stateStore.SaveAsync(stateKey, session).ConfigureAwait(false);

        _logger.LogInformation("MinIO webhook: Upload completed for session {UploadId}, size={Size}, etag={ETag}",
            uploadId, size, etag);

        // Defensive coding for external service: MinIO should always provide ETag for successful uploads,
        // but we handle null gracefully since this is third-party webhook data
        if (string.IsNullOrEmpty(etag))
        {
            _logger.LogError("MinIO webhook: Missing ETag for completed upload {UploadId} - MinIO may be misconfigured", uploadId);
            await _messageBus.TryPublishErrorAsync(
                serviceName: "asset",
                operation: "MinioWebhook",
                errorType: "MissingETag",
                message: "MinIO webhook did not include ETag for completed upload",
                details: new { UploadId = uploadId, Bucket = bucket, Key = key },
                severity: BeyondImmersion.BannouService.Events.ServiceErrorEventSeverity.Error).ConfigureAwait(false);
        }

        // Publish upload notification event for processing pipeline
        var uploadNotification = new AssetUploadNotification
        {
            UploadId = uploadId,
            Bucket = bucket,
            Key = key,
            ETag = etag?.Trim('"') ?? string.Empty, // Defensive: external service may omit ETag
            Size = size,
            ContentType = session.ContentType,
            Timestamp = DateTimeOffset.UtcNow
        };

        await _messageBus.TryPublishAsync("asset.upload.completed", uploadNotification).ConfigureAwait(false);

        _logger.LogDebug("MinIO webhook: Published upload completion event for {UploadId}", uploadId);
    }
}

/// <summary>
/// MinIO S3 event notification structure.
/// Based on AWS S3 event notification format which MinIO follows.
/// </summary>
public class MinioNotification
{
    [JsonPropertyName("Records")]
    public List<MinioEventRecord>? Records { get; set; }
}

/// <summary>
/// Individual event record from MinIO notification.
/// </summary>
public class MinioEventRecord
{
    [JsonPropertyName("eventVersion")]
    public string? EventVersion { get; set; }

    [JsonPropertyName("eventSource")]
    public string? EventSource { get; set; }

    [JsonPropertyName("awsRegion")]
    public string? AwsRegion { get; set; }

    [JsonPropertyName("eventTime")]
    public string? EventTime { get; set; }

    [JsonPropertyName("eventName")]
    public string? EventName { get; set; }

    [JsonPropertyName("userIdentity")]
    public MinioUserIdentity? UserIdentity { get; set; }

    [JsonPropertyName("requestParameters")]
    public Dictionary<string, string>? RequestParameters { get; set; }

    [JsonPropertyName("responseElements")]
    public Dictionary<string, string>? ResponseElements { get; set; }

    [JsonPropertyName("s3")]
    public MinioS3Info? S3 { get; set; }
}

/// <summary>
/// User identity information in MinIO event.
/// </summary>
public class MinioUserIdentity
{
    [JsonPropertyName("principalId")]
    public string? PrincipalId { get; set; }
}

/// <summary>
/// S3-specific information in MinIO event.
/// </summary>
public class MinioS3Info
{
    [JsonPropertyName("s3SchemaVersion")]
    public string? S3SchemaVersion { get; set; }

    [JsonPropertyName("configurationId")]
    public string? ConfigurationId { get; set; }

    [JsonPropertyName("bucket")]
    public MinioBucketInfo? Bucket { get; set; }

    [JsonPropertyName("object")]
    public MinioObjectInfo? Object { get; set; }
}

/// <summary>
/// Bucket information in MinIO event.
/// </summary>
public class MinioBucketInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("ownerIdentity")]
    public MinioUserIdentity? OwnerIdentity { get; set; }

    [JsonPropertyName("arn")]
    public string? Arn { get; set; }
}

/// <summary>
/// Object information in MinIO event.
/// </summary>
public class MinioObjectInfo
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("eTag")]
    public string? ETag { get; set; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("userMetadata")]
    public Dictionary<string, string>? UserMetadata { get; set; }

    [JsonPropertyName("sequencer")]
    public string? Sequencer { get; set; }
}

/// <summary>
/// Event published when an asset upload completes.
/// Used to trigger the processing pipeline.
/// </summary>
public class AssetUploadNotification
{
    public Guid UploadId { get; set; }
    public string Bucket { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string ETag { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}
