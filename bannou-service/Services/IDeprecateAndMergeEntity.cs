#nullable enable

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Marker interface for Category A deprecated entities that support merge operations.
/// Category A entities are world-building definitions (Species, Realm, RelationshipType, etc.)
/// that can be deprecated, merged into another definition, and eventually deleted.
/// </summary>
/// <remarks>
/// <para>
/// Structural tests validate that implementing services:
/// <list type="bullet">
///   <item>Have a merge endpoint using shared MergeDeprecatedRequest/MergeDeprecatedResponse</item>
///   <item>Publish *.merged events</item>
///   <item>Follow the delete-after-merge convention (publish *.deleted after *.merged when deleteAfterMerge is true)</item>
/// </list>
/// </para>
/// <para>
/// The Publish* validation (via Service_CallsAllGeneratedEventPublishers) separately ensures that
/// all declared event publishers are actually called, covering the *.merged event publication.
/// </para>
/// </remarks>
public interface IDeprecateAndMergeEntity;
