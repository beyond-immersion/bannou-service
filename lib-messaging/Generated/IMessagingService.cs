using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Messaging;

/// <summary>
/// Service interface for Messaging API
/// </summary>
public partial interface IMessagingService : IDaprService
{
        /// <summary>
        /// PublishEvent operation
        /// </summary>
        Task<(StatusCodes, PublishEventResponse?)> PublishEventAsync(PublishEventRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// CreateSubscription operation
        /// </summary>
        Task<(StatusCodes, CreateSubscriptionResponse?)> CreateSubscriptionAsync(CreateSubscriptionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// RemoveSubscription operation
        /// </summary>
        Task<(StatusCodes, RemoveSubscriptionResponse?)> RemoveSubscriptionAsync(RemoveSubscriptionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ListTopics operation
        /// </summary>
        Task<(StatusCodes, ListTopicsResponse?)> ListTopicsAsync(ListTopicsRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
