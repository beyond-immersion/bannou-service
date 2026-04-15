namespace BeyondImmersion.Bannou.SpriteTheory.Metadata;

/// <summary>
/// Defines a single equipment attachment point on a character model.
/// Maps a named slot to a specific mesh and skeleton bone for attachment.
/// </summary>
/// <param name="SlotName">Slot identifier (e.g., "head", "weapon_r", "shield_l").</param>
/// <param name="MeshPath">Path to the equipment mesh asset.</param>
/// <param name="BoneName">Skeleton bone to attach the equipment mesh to.</param>
public record EquipmentSlot(
    string SlotName,
    string MeshPath,
    string BoneName);
