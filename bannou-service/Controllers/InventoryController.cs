using BeyondImmersion.BannouService.Controllers.Messages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Service component responsible for inventory handling.
/// </summary>
[DaprController("inventory")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class InventoryController : Controller, IDaprController<InventoryService>
{
    /// <summary>
    /// Create new inventory (player, world, chest, etc).
    /// </summary>
    [DaprRoute("/create")]
    public async Task CreateInventory(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Add items to inventory.
    /// </summary>
    [DaprRoute("/add")]
    public async Task AddItems(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Update items in inventory.
    /// </summary>
    [DaprRoute("/update")]
    public async Task UpdateItem(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Remove items from inventory.
    /// </summary>
    [DaprRoute("/remove")]
    public async Task RemoveItems(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Transfer items from one inventory to another.
    /// </summary>
    [DaprRoute("/transfer")]
    public async Task TransferItems(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Destroy inventory.
    /// </summary>
    [DaprRoute("/destroy")]
    public async Task DestroyInventory(HttpContext context)
    {
        await Task.CompletedTask;
    }
}
