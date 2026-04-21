namespace BeyondImmersion.Bannou.SpriteTheory;

/// <summary>
/// Engine-agnostic asset identifier used across the sprite-composer family for model,
/// equipment mesh, and material references.
/// </summary>
/// <remarks>
/// <para>
/// The bridge resolves an <see cref="AssetReference"/> to an engine-specific handle at
/// load time (e.g., a Stride <c>Model</c>, a Godot <c>PackedScene</c>). For raw-FBX
/// workflows, engine bridges supporting filesystem asset sources register loose files
/// as single-asset bundles and use the bundle identifier as a stable handle — the
/// exact convention is the bridge's concern, not sprite-theory's.
/// </para>
/// <para>
/// Round-trips through <c>System.Text.Json</c> with default record serialization —
/// no custom converter required. Properties serialize as <c>bundleId</c>, <c>assetId</c>,
/// and <c>variantId</c> in camelCase JSON.
/// </para>
/// </remarks>
/// <param name="BundleId">
/// Asset bundle identifier. For raw-FBX workflows, engine bridges supporting filesystem
/// asset sources register loose files as single-asset bundles and use the bundle ID as
/// a stable handle.
/// </param>
/// <param name="AssetId">Asset identifier within the bundle.</param>
/// <param name="VariantId">
/// Optional variant identifier for assets with multiple visual variants (e.g., LOD levels,
/// palette swaps). Null for the default variant.
/// </param>
public record AssetReference(
    string BundleId,
    string AssetId,
    string? VariantId = null);
