// =============================================================================
// Quest Variable Provider Factory
// Creates QuestProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Quest.Caching;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Quest.Providers;

/// <summary>
/// Factory for creating QuestProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class QuestProviderFactory : IVariableProviderFactory
{
    private readonly IQuestDataCache _cache;

    /// <summary>
    /// Creates a new quest provider factory.
    /// </summary>
    public QuestProviderFactory(IQuestDataCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Quest;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        if (!characterId.HasValue)
        {
            return QuestProvider.Empty;
        }

        var quests = await _cache.GetActiveQuestsOrLoadAsync(characterId.Value, ct);
        return new QuestProvider(quests);
    }
}
