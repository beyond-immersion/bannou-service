using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Genesis;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Genesis.Tests;

/// <summary>
/// Unit tests for <see cref="GenesisCurrencyTransactionListener"/> — the microsecond-fast filter
/// that routes wallet mutations to the growth accumulator when they belong to genesis entities.
/// </summary>
public class GenesisCurrencyTransactionListenerTests
{
    private readonly GenesisGrowthState _state = new();
    private readonly Mock<ILogger<GenesisCurrencyTransactionListener>> _mockLogger = new();

    private GenesisCurrencyTransactionListener CreateListener() =>
        new GenesisCurrencyTransactionListener(_state, _mockLogger.Object);

    private static CurrencyTransactionNotification CreateNotification(
        Guid walletId,
        double amount = 100.0,
        TransactionType transactionType = TransactionType.Mint) =>
        new(
            WalletId: walletId,
            OwnerId: Guid.NewGuid(),
            OwnerType: EntityType.Other,
            CurrencyDefinitionId: Guid.NewGuid(),
            CurrencyCode: "mana",
            Amount: amount,
            NewBalance: amount,
            TransactionType: transactionType,
            OccurredAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task OnCurrencyCreditedAsync_UnknownWallet_DoesNothing()
    {
        var listener = CreateListener();
        var notification = CreateNotification(Guid.NewGuid());

        await listener.OnCurrencyCreditedAsync(notification, CancellationToken.None);

        Assert.Empty(_state.DrainAccumulator());
    }

    [Fact]
    public async Task OnCurrencyCreditedAsync_KnownWallet_BuffersCreditEntry()
    {
        var listener = CreateListener();
        var walletId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        _state.SetWalletMapping(walletId, new GenesisWalletMapping(
            EntityId: entityId,
            TemplateCode: "treasure_chest",
            WalletCode: "mana",
            GrowthMappings: new List<GenesisGrowthMapping>()));

        var notification = CreateNotification(walletId, amount: 42.0);

        await listener.OnCurrencyCreditedAsync(notification, CancellationToken.None);

        var drained = _state.DrainAccumulator();
        Assert.Single(drained);
        Assert.True(drained.ContainsKey(entityId));
        Assert.Single(drained[entityId]);
        var entry = drained[entityId][0];
        Assert.Equal("mana", entry.WalletCode);
        Assert.Equal(42.0, entry.Amount);
        Assert.Equal(GrowthDirection.Credit, entry.Direction);
    }

    [Fact]
    public async Task OnCurrencyDebitedAsync_UnknownWallet_DoesNothing()
    {
        var listener = CreateListener();
        var notification = CreateNotification(Guid.NewGuid());

        await listener.OnCurrencyDebitedAsync(notification, CancellationToken.None);

        Assert.Empty(_state.DrainAccumulator());
    }

    [Fact]
    public async Task OnCurrencyDebitedAsync_KnownWallet_BuffersDebitEntry()
    {
        var listener = CreateListener();
        var walletId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        _state.SetWalletMapping(walletId, new GenesisWalletMapping(
            EntityId: entityId,
            TemplateCode: "treasure_chest",
            WalletCode: "mana",
            GrowthMappings: new List<GenesisGrowthMapping>()));

        var notification = CreateNotification(walletId, amount: 15.0);

        await listener.OnCurrencyDebitedAsync(notification, CancellationToken.None);

        var drained = _state.DrainAccumulator();
        Assert.Single(drained);
        var entry = drained[entityId][0];
        Assert.Equal("mana", entry.WalletCode);
        Assert.Equal(15.0, entry.Amount);
        Assert.Equal(GrowthDirection.Debit, entry.Direction);
    }

    [Fact]
    public async Task OnCurrencyCreditedAsync_MultipleCreditsToSameWallet_AllBuffered()
    {
        var listener = CreateListener();
        var walletId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        _state.SetWalletMapping(walletId, new GenesisWalletMapping(
            EntityId: entityId,
            TemplateCode: "treasure_chest",
            WalletCode: "mana",
            GrowthMappings: new List<GenesisGrowthMapping>()));

        await listener.OnCurrencyCreditedAsync(CreateNotification(walletId, amount: 10.0), CancellationToken.None);
        await listener.OnCurrencyCreditedAsync(CreateNotification(walletId, amount: 20.0), CancellationToken.None);
        await listener.OnCurrencyCreditedAsync(CreateNotification(walletId, amount: 30.0), CancellationToken.None);

        var drained = _state.DrainAccumulator();
        Assert.Single(drained);
        Assert.Equal(3, drained[entityId].Count);
        Assert.Equal(60.0, drained[entityId].Sum(e => e.Amount));
        Assert.All(drained[entityId], e => Assert.Equal(GrowthDirection.Credit, e.Direction));
    }

    [Fact]
    public async Task OnCurrencyCreditedAsync_MixedKnownAndUnknownWallets_OnlyKnownBuffered()
    {
        var listener = CreateListener();
        var knownWallet = Guid.NewGuid();
        var unknownWallet = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        _state.SetWalletMapping(knownWallet, new GenesisWalletMapping(
            EntityId: entityId,
            TemplateCode: "treasure_chest",
            WalletCode: "mana",
            GrowthMappings: new List<GenesisGrowthMapping>()));

        await listener.OnCurrencyCreditedAsync(CreateNotification(unknownWallet, amount: 100.0), CancellationToken.None);
        await listener.OnCurrencyCreditedAsync(CreateNotification(knownWallet, amount: 50.0), CancellationToken.None);
        await listener.OnCurrencyCreditedAsync(CreateNotification(unknownWallet, amount: 200.0), CancellationToken.None);

        var drained = _state.DrainAccumulator();
        Assert.Single(drained);
        Assert.Single(drained[entityId]);
        Assert.Equal(50.0, drained[entityId][0].Amount);
    }
}
