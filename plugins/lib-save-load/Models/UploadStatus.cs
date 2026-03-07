namespace BeyondImmersion.BannouService.SaveLoad.Models;

/// <summary>
/// Status of an async upload operation.
/// </summary>
public enum UploadStatus
{
    /// <summary>
    /// Upload is pending/queued
    /// </summary>
    Pending,

    /// <summary>
    /// Upload completed successfully
    /// </summary>
    Complete,

    /// <summary>
    /// Upload failed
    /// </summary>
    Failed
}
