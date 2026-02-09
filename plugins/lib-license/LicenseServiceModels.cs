namespace BeyondImmersion.BannouService.License;

/// <summary>
/// Internal data models for LicenseService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class LicenseService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Internal storage model for board templates.
/// Stores grid layout configuration, contract reference, and adjacency settings.
/// </summary>
internal class BoardTemplateModel
{
    /// <summary>Unique identifier for the board template.</summary>
    public Guid BoardTemplateId { get; set; }

    /// <summary>Game service this template belongs to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Display name for the board template.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of the board template.</summary>
    public string? Description { get; set; }

    /// <summary>Width of the grid in cells.</summary>
    public int GridWidth { get; set; }

    /// <summary>Height of the grid in cells.</summary>
    public int GridHeight { get; set; }

    /// <summary>Grid positions where unlock paths can begin (bypass adjacency).</summary>
    public List<GridPositionEntry> StartingNodes { get; set; } = new();

    /// <summary>Contract template used for unlock execution.</summary>
    public Guid BoardContractTemplateId { get; set; }

    /// <summary>Grid traversal mode for adjacency checks.</summary>
    public AdjacencyMode AdjacencyMode { get; set; }

    /// <summary>Whether this template is available for new board creation.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When this template was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this template was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for license definitions (nodes on a board template).
/// Each definition maps a grid position to an item template with LP cost and prerequisites.
/// </summary>
internal class LicenseDefinitionModel
{
    /// <summary>Unique identifier for this license definition.</summary>
    public Guid LicenseDefinitionId { get; set; }

    /// <summary>Board template this definition belongs to.</summary>
    public Guid BoardTemplateId { get; set; }

    /// <summary>Unique code within the board template (e.g., "fire_mastery_1").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>X coordinate on the grid.</summary>
    public int PositionX { get; set; }

    /// <summary>Y coordinate on the grid.</summary>
    public int PositionY { get; set; }

    /// <summary>License point cost to unlock.</summary>
    public int LpCost { get; set; }

    /// <summary>Item template instantiated when this license is unlocked.</summary>
    public Guid ItemTemplateId { get; set; }

    /// <summary>License codes that must be unlocked anywhere on the board before this one.</summary>
    public List<string>? Prerequisites { get; set; }

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; set; }

    /// <summary>Optional metadata for contract template values or display hints.</summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>When this definition was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Internal storage model for board instances (character-specific board).
/// Links a character to a board template with an inventory container.
/// </summary>
internal class BoardInstanceModel
{
    /// <summary>Unique identifier for the board instance.</summary>
    public Guid BoardId { get; set; }

    /// <summary>Character who owns this board.</summary>
    public Guid CharacterId { get; set; }

    /// <summary>Board template this instance was created from.</summary>
    public Guid BoardTemplateId { get; set; }

    /// <summary>Game service scope for this board.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Inventory container holding unlocked license items.</summary>
    public Guid ContainerId { get; set; }

    /// <summary>When this board instance was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Cache model for board state stored in Redis.
/// Tracks which licenses have been unlocked on a specific board instance.
/// </summary>
internal class BoardCacheModel
{
    /// <summary>Board instance this cache represents.</summary>
    public Guid BoardId { get; set; }

    /// <summary>List of unlocked licenses with their positions and item references.</summary>
    public List<UnlockedLicenseEntry> UnlockedPositions { get; set; } = new();

    /// <summary>When this cache was last updated.</summary>
    public DateTimeOffset LastUpdated { get; set; }
}

/// <summary>
/// Entry in the board cache representing a single unlocked license.
/// </summary>
internal class UnlockedLicenseEntry
{
    /// <summary>License code that was unlocked.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>X coordinate on the grid.</summary>
    public int PositionX { get; set; }

    /// <summary>Y coordinate on the grid.</summary>
    public int PositionY { get; set; }

    /// <summary>Item instance created when this license was unlocked.</summary>
    public Guid ItemInstanceId { get; set; }

    /// <summary>When this license was unlocked.</summary>
    public DateTimeOffset UnlockedAt { get; set; }
}

/// <summary>
/// Internal helper for grid position storage in board templates.
/// </summary>
internal class GridPositionEntry
{
    /// <summary>X coordinate on the grid.</summary>
    public int X { get; set; }

    /// <summary>Y coordinate on the grid.</summary>
    public int Y { get; set; }
}
