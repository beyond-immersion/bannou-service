using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.History;

/// <summary>
/// Implementation of backstory/lore element storage with merge/replace semantics.
/// Handles the common pattern of storing typed elements that can be identified
/// by a composite (type, key) pair, with support for merging or replacing.
/// </summary>
/// <typeparam name="TBackstory">Type of the backstory container.</typeparam>
/// <typeparam name="TElement">Type of elements in the backstory.</typeparam>
public class BackstoryStorageHelper<TBackstory, TElement> : IBackstoryStorageHelper<TBackstory, TElement>
    where TBackstory : class, new()
    where TElement : class
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly string _stateStoreName;
    private readonly string _keyPrefix;
    private readonly IBackstoryElementMatcher<TElement> _elementMatcher;
    private readonly Func<TBackstory, string> _getEntityId;
    private readonly Action<TBackstory, string> _setEntityId;
    private readonly Func<TBackstory, List<TElement>> _getElements;
    private readonly Action<TBackstory, List<TElement>> _setElements;
    private readonly Func<TBackstory, long> _getCreatedAtUnix;
    private readonly Action<TBackstory, long> _setCreatedAtUnix;
    private readonly Func<TBackstory, long> _getUpdatedAtUnix;
    private readonly Action<TBackstory, long> _setUpdatedAtUnix;

    /// <summary>
    /// Creates a new BackstoryStorageHelper with the specified configuration.
    /// </summary>
    /// <param name="config">Configuration for the helper.</param>
    public BackstoryStorageHelper(BackstoryStorageConfiguration<TBackstory, TElement> config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        _stateStoreFactory = config.StateStoreFactory ?? throw new ArgumentNullException(nameof(config.StateStoreFactory));
        _stateStoreName = config.StateStoreName ?? throw new ArgumentNullException(nameof(config.StateStoreName));
        _keyPrefix = config.KeyPrefix ?? throw new ArgumentNullException(nameof(config.KeyPrefix));
        _elementMatcher = config.ElementMatcher ?? throw new ArgumentNullException(nameof(config.ElementMatcher));
        _getEntityId = config.GetEntityId ?? throw new ArgumentNullException(nameof(config.GetEntityId));
        _setEntityId = config.SetEntityId ?? throw new ArgumentNullException(nameof(config.SetEntityId));
        _getElements = config.GetElements ?? throw new ArgumentNullException(nameof(config.GetElements));
        _setElements = config.SetElements ?? throw new ArgumentNullException(nameof(config.SetElements));
        _getCreatedAtUnix = config.GetCreatedAtUnix ?? throw new ArgumentNullException(nameof(config.GetCreatedAtUnix));
        _setCreatedAtUnix = config.SetCreatedAtUnix ?? throw new ArgumentNullException(nameof(config.SetCreatedAtUnix));
        _getUpdatedAtUnix = config.GetUpdatedAtUnix ?? throw new ArgumentNullException(nameof(config.GetUpdatedAtUnix));
        _setUpdatedAtUnix = config.SetUpdatedAtUnix ?? throw new ArgumentNullException(nameof(config.SetUpdatedAtUnix));
    }

    /// <inheritdoc />
    public async Task<TBackstory?> GetAsync(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(entityId)) return null;

        var store = _stateStoreFactory.GetStore<TBackstory>(_stateStoreName);
        return await store.GetAsync($"{_keyPrefix}{entityId}", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<BackstoryOperationResult<TBackstory>> SetAsync(
        string entityId,
        IReadOnlyList<TElement> elements,
        bool replaceExisting,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(entityId)) throw new ArgumentNullException(nameof(entityId));
        if (elements == null) throw new ArgumentNullException(nameof(elements));

        var store = _stateStoreFactory.GetStore<TBackstory>(_stateStoreName);
        var key = $"{_keyPrefix}{entityId}";
        var existing = await store.GetAsync(key, cancellationToken);
        var isNew = existing == null;
        var nowUnix = TimestampHelper.NowUnixSeconds();

        TBackstory data;

        if (replaceExisting || isNew)
        {
            // Replace all elements
            data = new TBackstory();
            _setEntityId(data, entityId);
            _setElements(data, elements.Select(e => _elementMatcher.Clone(e)).ToList());
            _setCreatedAtUnix(data, isNew ? nowUnix : _getCreatedAtUnix(existing!));
            _setUpdatedAtUnix(data, nowUnix);
        }
        else
        {
            // Merge: update matching type+key pairs, add new ones
            data = existing!;
            var existingElements = _getElements(data);

            foreach (var newElement in elements)
            {
                var newType = _elementMatcher.GetElementType(newElement);
                var newKey = _elementMatcher.GetElementKey(newElement);

                var existingElement = existingElements.FirstOrDefault(e =>
                    _elementMatcher.GetElementType(e) == newType &&
                    _elementMatcher.GetElementKey(e) == newKey);

                if (existingElement != null)
                {
                    // Update existing element
                    _elementMatcher.CopyValues(newElement, existingElement);
                }
                else
                {
                    // Add new element
                    existingElements.Add(_elementMatcher.Clone(newElement));
                }
            }

            _setUpdatedAtUnix(data, nowUnix);
        }

        await store.SaveAsync(key, data, cancellationToken: cancellationToken);
        return new BackstoryOperationResult<TBackstory>(data, isNew);
    }

    /// <inheritdoc />
    public async Task<BackstoryOperationResult<TBackstory>> AddElementAsync(
        string entityId,
        TElement element,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(entityId)) throw new ArgumentNullException(nameof(entityId));
        if (element == null) throw new ArgumentNullException(nameof(element));

        var store = _stateStoreFactory.GetStore<TBackstory>(_stateStoreName);
        var key = $"{_keyPrefix}{entityId}";
        var existing = await store.GetAsync(key, cancellationToken);
        var isNew = existing == null;
        var nowUnix = TimestampHelper.NowUnixSeconds();

        TBackstory data;

        if (isNew)
        {
            // Create new backstory with single element
            data = new TBackstory();
            _setEntityId(data, entityId);
            _setElements(data, new List<TElement> { _elementMatcher.Clone(element) });
            _setCreatedAtUnix(data, nowUnix);
            _setUpdatedAtUnix(data, nowUnix);
        }
        else
        {
            // Add or update element in existing backstory
            data = existing!;
            var existingElements = _getElements(data);
            var elementType = _elementMatcher.GetElementType(element);
            var elementKey = _elementMatcher.GetElementKey(element);

            var existingElement = existingElements.FirstOrDefault(e =>
                _elementMatcher.GetElementType(e) == elementType &&
                _elementMatcher.GetElementKey(e) == elementKey);

            if (existingElement != null)
            {
                // Update existing element
                _elementMatcher.CopyValues(element, existingElement);
            }
            else
            {
                // Add new element
                existingElements.Add(_elementMatcher.Clone(element));
            }

            _setUpdatedAtUnix(data, nowUnix);
        }

        await store.SaveAsync(key, data, cancellationToken: cancellationToken);
        return new BackstoryOperationResult<TBackstory>(data, isNew);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(entityId)) return false;

        var store = _stateStoreFactory.GetStore<TBackstory>(_stateStoreName);
        var key = $"{_keyPrefix}{entityId}";

        var existing = await store.GetAsync(key, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        await store.DeleteAsync(key, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(entityId)) return false;

        var store = _stateStoreFactory.GetStore<TBackstory>(_stateStoreName);
        return await store.ExistsAsync($"{_keyPrefix}{entityId}", cancellationToken);
    }
}
