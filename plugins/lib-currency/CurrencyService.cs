using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-currency.tests")]

namespace BeyondImmersion.BannouService.Currency;

/// <summary>
/// Implementation of the Currency service.
/// This class contains the business logic for all Currency operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Request/Response models: bannou-service/Generated/Models/CurrencyModels.cs</item>
///   <item>Event models: bannou-service/Generated/Events/CurrencyEventsModels.cs</item>
///   <item>Lifecycle events: bannou-service/Generated/Events/CurrencyLifecycleEvents.cs</item>
/// </list>
/// </para>
/// <para>
/// Standard structure:
/// <list type="bullet">
///   <item>CurrencyService.cs (this file) - Business logic</item>
///   <item>CurrencyServiceEvents.cs - Event consumer handlers (generated)</item>
///   <item>Generated/CurrencyPermissionRegistration.cs - Permission registration (generated)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("currency", typeof(ICurrencyService), lifetime: ServiceLifetime.Scoped)]
public partial class CurrencyService : ICurrencyService
{
    private readonly IMessageBus _messageBus;
    private readonly IServiceNavigator _navigator;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<CurrencyService> _logger;
    private readonly CurrencyServiceConfiguration _configuration;

    private const string STATE_STORE = "currency-statestore";

    public CurrencyService(
        IMessageBus messageBus,
        IServiceNavigator navigator,
        IStateStoreFactory stateStoreFactory,
        ILogger<CurrencyService> logger,
        CurrencyServiceConfiguration configuration)
    {
        _messageBus = messageBus;
        _navigator = navigator;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
    }

