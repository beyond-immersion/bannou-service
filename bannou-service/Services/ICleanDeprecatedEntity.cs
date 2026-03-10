#nullable enable

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Marker interface for Category B deprecated entities that support cleanup sweeps.
/// Category B entities are content templates (ItemTemplate, QuestDefinition, etc.)
/// that can be deprecated and cleaned up when zero instances remain.
/// </summary>
/// <remarks>
/// <para>
/// Structural tests validate that implementing services:
/// <list type="bullet">
///   <item>Have a clean-deprecated endpoint using shared CleanDeprecatedRequest/CleanDeprecatedResponse</item>
///   <item>Use DeprecationCleanupHelper.ExecuteCleanupSweepAsync for standardized sweep processing</item>
/// </list>
/// </para>
/// <para>
/// See <c>bannou-service/Helpers/DeprecationCleanupHelper.cs</c> for the shared sweep implementation.
/// </para>
/// </remarks>
public interface ICleanDeprecatedEntity;
