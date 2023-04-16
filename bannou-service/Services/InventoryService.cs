namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for inventory handling.
/// </summary>
[DaprService("inventory")]
public class InventoryService : IDaprService
{
    /// <summary>
    /// Create new inventory (player, world, chest, etc).
    /// </summary>
    public async Task CreateInventory() => await Task.CompletedTask;

    /// <summary>
    /// Add items to inventory.
    /// </summary>
    public async Task AddItems() => await Task.CompletedTask;

    /// <summary>
    /// Update items in inventory.
    /// </summary>
    public async Task UpdateItem() => await Task.CompletedTask;

    /// <summary>
    /// Remove items from inventory.
    /// </summary>
    public async Task RemoveItems() => await Task.CompletedTask;

    /// <summary>
    /// Transfer items from one inventory to another.
    /// </summary>
    public async Task TransferItems() => await Task.CompletedTask;

    /// <summary>
    /// Destroy inventory.
    /// </summary>
    public async Task DestroyInventory() => await Task.CompletedTask;
}
