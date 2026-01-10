namespace BeyondImmersion.BannouService.History;

/// <summary>
/// Utility methods for Unix timestamp conversions in history storage.
/// Provides consistent timestamp handling across all history services.
/// </summary>
public static class TimestampHelper
{
    /// <summary>
    /// Converts a DateTimeOffset to Unix timestamp in seconds.
    /// </summary>
    /// <param name="datetime">The datetime to convert.</param>
    /// <returns>Unix timestamp in seconds.</returns>
    public static long ToUnixSeconds(DateTimeOffset datetime)
        => datetime.ToUnixTimeSeconds();

    /// <summary>
    /// Converts a Unix timestamp in seconds to DateTimeOffset.
    /// </summary>
    /// <param name="unixSeconds">Unix timestamp in seconds.</param>
    /// <returns>The corresponding DateTimeOffset in UTC.</returns>
    public static DateTimeOffset FromUnixSeconds(long unixSeconds)
        => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

    /// <summary>
    /// Converts a DateTimeOffset to Unix timestamp in milliseconds.
    /// </summary>
    /// <param name="datetime">The datetime to convert.</param>
    /// <returns>Unix timestamp in milliseconds.</returns>
    public static long ToUnixMilliseconds(DateTimeOffset datetime)
        => datetime.ToUnixTimeMilliseconds();

    /// <summary>
    /// Converts a Unix timestamp in milliseconds to DateTimeOffset.
    /// </summary>
    /// <param name="unixMs">Unix timestamp in milliseconds.</param>
    /// <returns>The corresponding DateTimeOffset in UTC.</returns>
    public static DateTimeOffset FromUnixMilliseconds(long unixMs)
        => DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

    /// <summary>
    /// Gets the current UTC time as Unix timestamp in seconds.
    /// </summary>
    /// <returns>Current Unix timestamp in seconds.</returns>
    public static long NowUnixSeconds()
        => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// Gets the current UTC time as Unix timestamp in milliseconds.
    /// </summary>
    /// <returns>Current Unix timestamp in milliseconds.</returns>
    public static long NowUnixMilliseconds()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Converts nullable DateTimeOffset to nullable Unix seconds.
    /// </summary>
    /// <param name="datetime">The nullable datetime to convert.</param>
    /// <returns>Unix timestamp in seconds, or null if input is null.</returns>
    public static long? ToUnixSecondsOrNull(DateTimeOffset? datetime)
        => datetime?.ToUnixTimeSeconds();

    /// <summary>
    /// Converts nullable Unix seconds to nullable DateTimeOffset.
    /// </summary>
    /// <param name="unixSeconds">The nullable Unix timestamp.</param>
    /// <returns>DateTimeOffset, or null if input is null or zero.</returns>
    public static DateTimeOffset? FromUnixSecondsOrNull(long? unixSeconds)
        => unixSeconds.HasValue && unixSeconds.Value != 0
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value)
            : null;
}
