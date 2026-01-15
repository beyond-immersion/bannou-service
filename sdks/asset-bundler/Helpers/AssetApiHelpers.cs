using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Asset;

namespace BeyondImmersion.Bannou.AssetBundler.Helpers;

/// <summary>
/// Shared helpers for asset bundler API operations.
/// Consolidates common enum parsing and error handling patterns.
/// </summary>
public static class AssetApiHelpers
{
    #region Enum Parsing

    /// <summary>
    /// Parses a realm string to the Realm enum.
    /// </summary>
    /// <param name="realm">Realm string (case-insensitive).</param>
    /// <param name="defaultValue">Value to return if realm is null/empty (default: null).</param>
    /// <returns>Parsed realm or default value.</returns>
    public static Realm? ParseRealm(string? realm, Realm? defaultValue = null)
    {
        if (string.IsNullOrEmpty(realm))
            return defaultValue;

        return realm.ToLowerInvariant() switch
        {
            "omega" => Realm.Omega,
            "arcadia" => Realm.Arcadia,
            "fantasia" => Realm.Fantasia,
            "shared" => Realm.Shared,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Parses a realm string to the Realm enum, throwing on unknown values.
    /// </summary>
    /// <param name="realm">Realm string (case-insensitive).</param>
    /// <param name="defaultValue">Value to return if realm is null/empty.</param>
    /// <returns>Parsed realm.</returns>
    /// <exception cref="ArgumentException">Thrown if realm string is unrecognized.</exception>
    public static Realm ParseRealmRequired(string? realm, Realm defaultValue = Realm.Omega)
    {
        if (string.IsNullOrEmpty(realm))
            return defaultValue;

        return realm.ToLowerInvariant() switch
        {
            "omega" => Realm.Omega,
            "arcadia" => Realm.Arcadia,
            "fantasia" => Realm.Fantasia,
            "shared" => Realm.Shared,
            _ => throw new ArgumentException($"Unknown realm: {realm}", nameof(realm))
        };
    }

    /// <summary>
    /// Parses a bundle type string to the BundleType enum.
    /// </summary>
    /// <param name="bundleType">Bundle type string (case-insensitive).</param>
    /// <returns>Parsed bundle type or null if unrecognized.</returns>
    public static BundleType? ParseBundleType(string? bundleType)
    {
        if (string.IsNullOrEmpty(bundleType))
            return null;

        return bundleType.ToLowerInvariant() switch
        {
            "source" => BundleType.Source,
            "metabundle" => BundleType.Metabundle,
            _ => null
        };
    }

    /// <summary>
    /// Parses a lifecycle status string to the BundleLifecycle enum.
    /// </summary>
    /// <param name="status">Status string (case-insensitive).</param>
    /// <returns>Parsed lifecycle status or null if unrecognized.</returns>
    public static BundleLifecycle? ParseLifecycle(string? status)
    {
        if (string.IsNullOrEmpty(status))
            return null;

        return status.ToLowerInvariant() switch
        {
            "active" => BundleLifecycle.Active,
            "deleted" => BundleLifecycle.Deleted,
            "processing" => BundleLifecycle.Processing,
            _ => null
        };
    }

    #endregion

    #region Error Handling

    /// <summary>
    /// Creates an exception from an API error response.
    /// </summary>
    /// <typeparam name="TResponse">Response type.</typeparam>
    /// <param name="operation">Description of the operation that failed.</param>
    /// <param name="response">The API response containing the error.</param>
    /// <returns>Exception with formatted error message.</returns>
    public static InvalidOperationException CreateApiException<TResponse>(
        string operation,
        ApiResponse<TResponse> response)
        where TResponse : class
    {
        var errorCode = response.Error?.ResponseCode ?? 500;
        var errorMessage = response.Error?.Message ?? "Unknown error";
        return new InvalidOperationException($"Failed to {operation}: {errorCode} - {errorMessage}");
    }

    /// <summary>
    /// Throws an exception if the API response indicates failure.
    /// </summary>
    /// <typeparam name="TResponse">Response type.</typeparam>
    /// <param name="response">The API response to check.</param>
    /// <param name="operation">Description of the operation (for error message).</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if response is not successful or result is null.</exception>
    public static TResponse EnsureSuccess<TResponse>(
        ApiResponse<TResponse> response,
        string operation)
        where TResponse : class
    {
        if (!response.IsSuccess || response.Result == null)
        {
            throw CreateApiException(operation, response);
        }

        return response.Result;
    }

    #endregion
}
