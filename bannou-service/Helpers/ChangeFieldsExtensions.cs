namespace BeyondImmersion.BannouService;

/// <summary>
/// Extension methods for changeFields-based partial update request support.
/// Used by Update endpoints to distinguish "field absent" (don't change)
/// from "field explicitly set to null" (clear the value).
///
/// The changeFields collection is populated automatically by property setter
/// tracking on generated Update/Modify/Set request models (post-processed
/// by postprocess-change-tracking.py). Service code uses these extension
/// methods to check which fields the caller intended to set.
///
/// See GitHub Issue #722 for the systemic design.
/// </summary>
public static class ChangeFieldsExtensions
{
    /// <summary>
    /// Check whether a specific field was explicitly set on the request.
    /// Returns false when changeFields is null or empty (degenerate case —
    /// normally changeFields is always populated by setter tracking during
    /// deserialization).
    /// </summary>
    /// <param name="changeFields">The ChangeFields collection from the request (may be null).</param>
    /// <param name="fieldName">The camelCase property name to check (case-insensitive).</param>
    /// <returns>True if the field was explicitly set; false otherwise.</returns>
    public static bool IsFieldSet(this ICollection<string>? changeFields, string fieldName)
    {
        if (changeFields == null || changeFields.Count == 0)
            return false;

        return changeFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);
    }
}
