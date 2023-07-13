using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for login queue handling.
/// 
/// Can have the login service make an additional endpoint for `/login_{service_guid}` which would be unique to this service instance.
/// This would allow communication with a specific instance, in order to count down a queue position. This means the bulk of the work
/// for the queue could be done internally, with minimal traffic to the datastore only to relay metrics used to bring instances up and
/// down as demand dictates.
/// 
/// This would mean that a bad actor could spam a specific login server instance, but if that doesn't actually increase the internal
/// network traffic on each request, I'm not sure it matters.
/// </summary>
[DaprService("login", lifetime: ServiceLifetime.Singleton)]
public sealed class LoginService : IDaprService
{
    public class QueueResult
    {
        public enum QueueStates
        {
            None,
            Enqueued,
            Finished,
            Blocked
        };

        public QueueStates QueueState { get; set; }
        public string? QueueID { get; set; }
        public DateTime? EnqueueTime { get; set; }
        public int QueuePosition { get; set; }
    }

    private ConcurrentDictionary<string, LeakyBucket> ClientRequestTracker { get; } = new ConcurrentDictionary<string, LeakyBucket>();
    private List<string> BannedClients { get; } = new List<string>();

    bool IDaprService.OnLoad()
    {
        Dapr.Client.GetConfigurationResponse configurationResponse = Program.DaprClient.GetConfiguration("service config", new[] { "login_queue_duration" }).Result;
        if (configurationResponse != null)
        {
            foreach (KeyValuePair<string, Dapr.Client.ConfigurationItem> configKvp in configurationResponse.Items)
            {
                if (configKvp.Key == "login_queue_duration")
                {
                    // use configured processing rate for login queue, if set (per second)
                    if (int.TryParse(configKvp.Value.Value, out var queueDuration))
                        QueueDuration = queueDuration;
                }
            }
        }

        return true;
    }

    void IDaprService.OnExit()
    {

    }

    /// <summary>
    /// 
    /// </summary>
    public async Task<QueueResult> EnqueueClient(string? clientHost = null, string? clientID = null)
    {
        QueueResult loginResult;
        if (!string.IsNullOrWhiteSpace(clientHost))
        {
            var reqBucket = ClientRequestTracker.AddOrUpdate(
                clientHost,
                new LeakyBucket(new LeakyBucket.BucketConfiguration() { LeakRate = 60, LeakRateTimeSpan = TimeSpan.FromMinutes(1) }),
                (t, s) =>
                {
                    _ = s.Increment();
                    return s;
                });

            if (reqBucket.Count >= 100)
                BannedClients.Add(clientHost);

            if (BannedClients.Contains(clientHost, StringComparer.InvariantCultureIgnoreCase))
            {
                var queueState = QueueResult.QueueStates.Blocked;
                loginResult = new QueueResult()
                {
                    QueueState = queueState,
                    QueueID = clientID
                };
                return loginResult;
            }
        }

        if (clientID != null)
        {
            var loginQueueCopy = LoginQueue.ToArray();
            for (var i = 0; i < loginQueueCopy.Length; i++)
            {
                var queueItem = loginQueueCopy[i];
                if (queueItem.QueueID != clientID)
                    continue;

                var queueState = QueueResult.QueueStates.Enqueued;
                var queueTime = DateTimeOffset.FromUnixTimeSeconds(queueItem.EnqueueTime).DateTime;
                var queuePosition = i;

                var queueFinished = QueueDuration == -1 || DateTimeOffset.FromUnixTimeSeconds(queueItem.EnqueueTime + QueueDuration).DateTime < DateTime.UtcNow;
                if (queueFinished)
                {
                    queueState = QueueResult.QueueStates.Finished;
                    queuePosition = 0;
                }

                loginResult = new QueueResult()
                {
                    QueueState = queueState,
                    QueueID = clientID,
                    EnqueueTime = queueTime,
                    QueuePosition = queuePosition
                };

                return loginResult;
            }
        }

        clientID = Guid.NewGuid().ToString().ToLower();
        LoginQueue.Add(new QueueItem()
        {
            QueueID = clientID,
            EnqueueTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        loginResult = new QueueResult()
        {
            QueueState = QueueDuration > 0 ? QueueResult.QueueStates.Finished : QueueResult.QueueStates.Enqueued,
            QueueID = clientID,
            EnqueueTime = DateTime.UtcNow
        };

        return loginResult;
    }
}
