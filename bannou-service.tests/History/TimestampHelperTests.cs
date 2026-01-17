using BeyondImmersion.BannouService.History;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Tests.History;

/// <summary>
/// Unit tests for TimestampHelper.
/// </summary>
[Collection("unit tests")]
public class TimestampHelperTests : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public TimestampHelperTests(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TimestampHelperTests>.Instance;
    }

    [Fact]
    public void ToUnixSeconds_ConvertsKnownDate()
    {
        // 2020-01-01 00:00:00 UTC = 1577836800 seconds since Unix epoch
        var date = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = TimestampHelper.ToUnixSeconds(date);
        Assert.Equal(1577836800L, result);
    }

    [Fact]
    public void FromUnixSeconds_ConvertsKnownTimestamp()
    {
        var result = TimestampHelper.FromUnixSeconds(1577836800L);
        var expected = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToUnixSeconds_FromUnixSeconds_RoundTrip()
    {
        var original = new DateTimeOffset(2023, 6, 15, 14, 30, 45, TimeSpan.Zero);
        var unixSeconds = TimestampHelper.ToUnixSeconds(original);
        var roundTripped = TimestampHelper.FromUnixSeconds(unixSeconds);

        // Note: Unix seconds lose sub-second precision
        Assert.Equal(original.Year, roundTripped.Year);
        Assert.Equal(original.Month, roundTripped.Month);
        Assert.Equal(original.Day, roundTripped.Day);
        Assert.Equal(original.Hour, roundTripped.Hour);
        Assert.Equal(original.Minute, roundTripped.Minute);
        Assert.Equal(original.Second, roundTripped.Second);
    }

    [Fact]
    public void ToUnixMilliseconds_ConvertsKnownDate()
    {
        var date = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = TimestampHelper.ToUnixMilliseconds(date);
        Assert.Equal(1577836800000L, result);
    }

    [Fact]
    public void FromUnixMilliseconds_ConvertsKnownTimestamp()
    {
        var result = TimestampHelper.FromUnixMilliseconds(1577836800000L);
        var expected = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToUnixMilliseconds_PreservesMilliseconds()
    {
        var date = new DateTimeOffset(2020, 1, 1, 0, 0, 0, 500, TimeSpan.Zero);
        var unixMs = TimestampHelper.ToUnixMilliseconds(date);
        var roundTripped = TimestampHelper.FromUnixMilliseconds(unixMs);

        Assert.Equal(date.Millisecond, roundTripped.Millisecond);
    }

    [Fact]
    public void NowUnixSeconds_ReturnsCurrentTimestamp()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var result = TimestampHelper.NowUnixSeconds();
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.InRange(result, before, after);
    }

    [Fact]
    public void NowUnixMilliseconds_ReturnsCurrentTimestamp()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = TimestampHelper.NowUnixMilliseconds();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.InRange(result, before, after);
    }

    [Fact]
    public void ToUnixSecondsOrNull_WithNull_ReturnsNull()
    {
        DateTimeOffset? nullDate = null;
        var result = TimestampHelper.ToUnixSecondsOrNull(nullDate);
        Assert.Null(result);
    }

    [Fact]
    public void ToUnixSecondsOrNull_WithValue_ReturnsUnixSeconds()
    {
        DateTimeOffset? date = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = TimestampHelper.ToUnixSecondsOrNull(date);
        Assert.Equal(1577836800L, result);
    }

    [Fact]
    public void FromUnixSecondsOrNull_WithNull_ReturnsNull()
    {
        long? nullTimestamp = null;
        var result = TimestampHelper.FromUnixSecondsOrNull(nullTimestamp);
        Assert.Null(result);
    }

    [Fact]
    public void FromUnixSecondsOrNull_WithZero_ReturnsNull()
    {
        long? zeroTimestamp = 0;
        var result = TimestampHelper.FromUnixSecondsOrNull(zeroTimestamp);
        Assert.Null(result);
    }

    [Fact]
    public void FromUnixSecondsOrNull_WithValue_ReturnsDateTimeOffset()
    {
        long? timestamp = 1577836800L;
        var result = TimestampHelper.FromUnixSecondsOrNull(timestamp);
        var expected = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void UnixEpoch_IsJanuary1970()
    {
        var result = TimestampHelper.FromUnixSeconds(0);
        Assert.Equal(1970, result.Year);
        Assert.Equal(1, result.Month);
        Assert.Equal(1, result.Day);
    }
}
