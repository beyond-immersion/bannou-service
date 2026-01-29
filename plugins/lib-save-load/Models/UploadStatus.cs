namespace BeyondImmersion.BannouService.SaveLoad.Models;

/// <summary>
/// Status of an async upload operation.
/// </summary>
public enum UploadStatus
{
    /// <summary>
    /// Upload is pending/queued
    /// </summary>
    PENDING,

    /// <summary>
    /// Upload completed successfully
    /// </summary>
    COMPLETE,

    /// <summary>
    /// Upload failed
    /// </summary>
    FAILED
}
