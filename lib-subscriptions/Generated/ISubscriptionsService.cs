using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Subscriptions;

/// <summary>
/// Service interface for Subscriptions API
/// </summary>
public partial interface ISubscriptionsService : IDaprService
{
        /// <summary>
        /// GetAccountSubscriptions operation
        /// </summary>
        Task<(StatusCodes, SubscriptionListResponse?)> GetAccountSubscriptionsAsync(GetAccountSubscriptionsRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetCurrentSubscriptions operation
        /// </summary>
        Task<(StatusCodes, CurrentSubscriptionsResponse?)> GetCurrentSubscriptionsAsync(GetCurrentSubscriptionsRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetSubscription operation
        /// </summary>
        Task<(StatusCodes, SubscriptionInfo?)> GetSubscriptionAsync(GetSubscriptionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// CreateSubscription operation
        /// </summary>
        Task<(StatusCodes, SubscriptionInfo?)> CreateSubscriptionAsync(CreateSubscriptionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateSubscription operation
        /// </summary>
        Task<(StatusCodes, SubscriptionInfo?)> UpdateSubscriptionAsync(UpdateSubscriptionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// CancelSubscription operation
        /// </summary>
        Task<(StatusCodes, SubscriptionInfo?)> CancelSubscriptionAsync(CancelSubscriptionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// RenewSubscription operation
        /// </summary>
        Task<(StatusCodes, SubscriptionInfo?)> RenewSubscriptionAsync(RenewSubscriptionRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
