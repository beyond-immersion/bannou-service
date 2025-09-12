using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Misc;

/// <summary>
/// Implements a leaky bucket rate limiting algorithm using a concurrent queue.
/// Items are added to the bucket and automatically leak out at a configured rate.
/// </summary>
public class LeakyBucket : ConcurrentQueue<DateTime>, IDisposable
{
    /// <summary>
    /// Configuration settings for the leaky bucket behavior.
    /// </summary>
    public class BucketConfiguration
    {
        /// <summary>
        /// Time interval between leak operations.
        /// </summary>
        public TimeSpan LeakRateTimeSpan { get; set; }

        /// <summary>
        /// Number of items to leak out per leak operation.
        /// </summary>
        public int LeakRate { get; set; }
    }

    private readonly ConcurrentQueue<DateTime> _items;
    private readonly BucketConfiguration _configuration;
    private readonly Task _leakTask;

    /// <summary>
    /// Gets a value indicating whether the bucket is read-only (always false).
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Releases all resources used by the LeakyBucket.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _leakTask.Dispose();
    }

    private LeakyBucket() { }
    /// <summary>
    /// Initializes a new instance of the LeakyBucket with the specified configuration.
    /// </summary>
    /// <param name="bucketConfiguration">Configuration for leak rate and timing.</param>
    public LeakyBucket(BucketConfiguration bucketConfiguration)
        : base()
    {
        _items = new ConcurrentQueue<DateTime>();
        _configuration = bucketConfiguration;
        _leakTask = Task.Factory.StartNew(Leak);
    }

    /// <summary>
    /// Adds a new item to the bucket with the current timestamp.
    /// </summary>
    /// <returns>The current number of items in the bucket after adding.</returns>
    public int Increment()
    {
        _items.Enqueue(DateTime.UtcNow);
        return _items.Count;
    }

    private void Leak()
    {
        while (Count == 0)
            Thread.Sleep(1000);

        while (true)
        {
            Thread.Sleep(_configuration.LeakRateTimeSpan);
            for (var i = 0; i < Count && i < _configuration.LeakRate; i++)
                TryDequeue(out _);
        }
    }
}
