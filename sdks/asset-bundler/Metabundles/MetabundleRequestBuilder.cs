namespace BeyondImmersion.Bannou.AssetBundler.Metabundles;

/// <summary>
/// Fluent builder for metabundle requests.
/// </summary>
public sealed class MetabundleRequestBuilder
{
    private string? _metabundleId;
    private readonly List<string> _sourceBundleIds = new();
    private readonly List<string> _standaloneAssetIds = new();
    private readonly List<string> _assetFilter = new();
    private string? _version;
    private string? _owner;
    private string? _realm;

    /// <summary>
    /// Sets the metabundle ID.
    /// </summary>
    /// <param name="metabundleId">Unique metabundle identifier.</param>
    /// <returns>This builder for chaining.</returns>
    public MetabundleRequestBuilder WithId(string metabundleId)
    {
        _metabundleId = metabundleId;
        return this;
    }

    /// <summary>
    /// Adds a source bundle to include.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <returns>This builder for chaining.</returns>
    public MetabundleRequestBuilder AddSourceBundle(string bundleId)
    {
        _sourceBundleIds.Add(bundleId);
        return this;
    }

    /// <summary>
    /// Adds multiple source bundles.
    /// </summary>
    /// <param name="bundleIds">Bundle identifiers.</param>
    /// <returns>This builder for chaining.</returns>
    public MetabundleRequestBuilder AddSourceBundles(IEnumerable<string> bundleIds)
    {
        _sourceBundleIds.AddRange(bundleIds);
        return this;
    }

    /// <summary>
    /// Adds a standalone asset to include.
    /// </summary>
    /// <param name="assetId">Asset identifier.</param>
    /// <returns>This builder for chaining.</returns>
    public MetabundleRequestBuilder AddStandaloneAsset(string assetId)
    {
        _standaloneAssetIds.Add(assetId);
        return this;
    }

    /// <summary>
    /// Sets an asset filter to include only specific assets.
    /// </summary>
    /// <param name="assetIds">Asset identifiers to include.</param>
    /// <returns>This builder for chaining.</returns>
    public MetabundleRequestBuilder WithAssetFilter(IEnumerable<string> assetIds)
    {
        _assetFilter.AddRange(assetIds);
        return this;
    }

    /// <summary>
    /// Sets the version string.
    /// </summary>
    /// <param name="version">Version string.</param>
    /// <returns>This builder for chaining.</returns>
    public MetabundleRequestBuilder WithVersion(string version)
    {
        _version = version;
        return this;
    }

    /// <summary>
    /// Sets the owner identifier.
    /// </summary>
    /// <param name="owner">Owner identifier.</param>
    /// <returns>This builder for chaining.</returns>
    public MetabundleRequestBuilder WithOwner(string owner)
    {
        _owner = owner;
        return this;
    }

    /// <summary>
    /// Sets the target realm.
    /// </summary>
    /// <param name="realm">Realm identifier.</param>
    /// <returns>This builder for chaining.</returns>
    public MetabundleRequestBuilder WithRealm(string realm)
    {
        _realm = realm;
        return this;
    }

    /// <summary>
    /// Builds the metabundle request.
    /// </summary>
    /// <returns>The constructed request.</returns>
    /// <exception cref="InvalidOperationException">If required fields are missing.</exception>
    public MetabundleRequest Build()
    {
        if (string.IsNullOrEmpty(_metabundleId))
            throw new InvalidOperationException("MetabundleId is required");

        if (_sourceBundleIds.Count == 0 && _standaloneAssetIds.Count == 0)
            throw new InvalidOperationException("At least one source bundle or standalone asset required");

        return new MetabundleRequest
        {
            MetabundleId = _metabundleId,
            SourceBundleIds = _sourceBundleIds,
            StandaloneAssetIds = _standaloneAssetIds.Count > 0 ? _standaloneAssetIds : null,
            AssetFilter = _assetFilter.Count > 0 ? _assetFilter : null,
            Version = _version,
            Owner = _owner,
            Realm = _realm
        };
    }
}
