using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for inventory handling.
/// </summary>
[DaprService("inventory")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class InventoryService : IDaprService
{
    /// <summary>
    /// Create new inventory (player, world, chest, etc).
    /// </summary>
    [ServiceRoute("/create")]
    public async Task CreateInventory(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Add items to inventory.
    /// </summary>
    [ServiceRoute("/add")]
    public async Task AddItems(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Update items in inventory.
    /// </summary>
    [ServiceRoute("/update")]
    public async Task UpdateItem(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Remove items from inventory.
    /// </summary>
    [ServiceRoute("/remove")]
    public async Task RemoveItems(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Transfer items from one inventory to another.
    /// </summary>
    [ServiceRoute("/transfer")]
    public async Task TransferItems(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Destroy inventory.
    /// </summary>
    [ServiceRoute("/destroy")]
    public async Task DestroyInventory(HttpContext context)
    {
        await Task.CompletedTask;
    }
}
