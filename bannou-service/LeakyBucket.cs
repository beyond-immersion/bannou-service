using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService;

public class LeakyBucket : ConcurrentQueue<DateTime>, IDisposable
{
    public class BucketConfiguration
    {
        public TimeSpan LeakRateTimeSpan { get; set; }
        public int LeakRate { get; set; }
    }

    private readonly ConcurrentQueue<DateTime> _items;
    private readonly BucketConfiguration _configuration;
    private readonly Task _leakTask;

    public bool IsReadOnly => false;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _leakTask.Dispose();
    }

    private LeakyBucket() { }
    public LeakyBucket(BucketConfiguration bucketConfiguration)
        : base()
    {
        _items = new ConcurrentQueue<DateTime>();
        _configuration = bucketConfiguration;
        _leakTask = Task.Factory.StartNew(Leak);
    }

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
