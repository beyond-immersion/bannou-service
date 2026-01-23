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

[assembly: InternalsVisibleTo("lib-inventory.tests")]

namespace BeyondImmersion.BannouService.Inventory;

/// <summary>
/// Implementation of the Inventory service.
/// This class contains the business logic for all Inventory operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Request/Response models: bannou-service/Generated/Models/InventoryModels.cs</item>
///   <item>Event models: bannou-service/Generated/Events/InventoryEventsModels.cs</item>
///   <item>Lifecycle events: bannou-service/Generated/Events/InventoryLifecycleEvents.cs</item>
/// </list>
/// </para>
/// <para>
/// Standard structure:
/// <list type="bullet">
///   <item>InventoryService.cs (this file) - Business logic</item>
///   <item>InventoryServiceEvents.cs - Event consumer handlers (generated)</item>
///   <item>Generated/InventoryPermissionRegistration.cs - Permission registration (generated)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("inventory", typeof(IInventoryService), lifetime: ServiceLifetime.Scoped)]
public partial class InventoryService : IInventoryService
{
    private readonly IMessageBus _messageBus;
    private readonly IServiceNavigator _navigator;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<InventoryService> _logger;
    private readonly InventoryServiceConfiguration _configuration;

    private const string STATE_STORE = "inventory-statestore";

    public InventoryService(
        IMessageBus messageBus,
        IServiceNavigator navigator,
        IStateStoreFactory stateStoreFactory,
        ILogger<InventoryService> logger,
        InventoryServiceConfiguration configuration)
    {
        _messageBus = messageBus;
        _navigator = navigator;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
    }

