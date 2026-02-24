using System.Text.Json;

namespace BeyondImmersion.BannouService.Currency;

/// <summary>
/// Internal data models for CurrencyService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>When to add models here:</b>
/// <list type="bullet">
///   <item>Storage models for state stores (different from API request/response types)</item>
///   <item>Cache entry structures</item>
///   <item>Internal DTOs for service-to-service communication not exposed in API</item>
///   <item>Helper records for intermediate processing</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class CurrencyService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Shared state store key prefixes used by CurrencyService and CurrencyAutogainTaskService.
/// </summary>
internal static class CurrencyKeys
{
    internal const string DEF_PREFIX = "def:";
    internal const string DEF_CODE_INDEX = "def-code:";
    internal const string ALL_DEFS_KEY = "all-defs";
    internal const string WALLET_PREFIX = "wallet:";
    internal const string WALLET_OWNER_INDEX = "wallet-owner:";
    internal const string BALANCE_PREFIX = "bal:";
    internal const string BALANCE_WALLET_INDEX = "bal-wallet:";
    internal const string BALANCE_CURRENCY_INDEX = "bal-currency:";
    internal const string TX_PREFIX = "tx:";
    internal const string TX_WALLET_INDEX = "tx-wallet:";
    internal const string TX_REF_INDEX = "tx-ref:";
    internal const string HOLD_PREFIX = "hold:";
    internal const string HOLD_WALLET_INDEX = "hold-wallet:";
}

/// <summary>
/// Internal model for currency definitions.
/// Used by both CurrencyService and CurrencyAutogainTaskService.
/// </summary>
internal class CurrencyDefinitionModel
{
    public Guid DefinitionId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public CurrencyScope Scope { get; set; } = CurrencyScope.Global;
    public List<Guid>? RealmsAvailable { get; set; }
    public CurrencyPrecision Precision { get; set; } = CurrencyPrecision.Integer;
    public bool Transferable { get; set; } = true;
    public bool Tradeable { get; set; } = true;
    public bool? AllowNegative { get; set; }
    public double? PerWalletCap { get; set; }
    public CapOverflowBehavior? CapOverflowBehavior { get; set; }
    public double? GlobalSupplyCap { get; set; }
    public double? DailyEarnCap { get; set; }
    public double? WeeklyEarnCap { get; set; }
    public string? EarnCapResetTime { get; set; }
    public bool AutogainEnabled { get; set; }
    public AutogainMode? AutogainMode { get; set; }
    public double? AutogainAmount { get; set; }
    public string? AutogainInterval { get; set; }
    public double? AutogainCap { get; set; }
    public bool Expires { get; set; }
    public ExpirationPolicy? ExpirationPolicy { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? ExpirationDuration { get; set; }
    public Guid? SeasonId { get; set; }
    public bool LinkedToItem { get; set; }
    public Guid? LinkedItemTemplateId { get; set; }
    public ItemLinkageMode? LinkageMode { get; set; }
    public bool IsBaseCurrency { get; set; }
    public double? ExchangeRateToBase { get; set; }
    public DateTimeOffset? ExchangeRateUpdatedAt { get; set; }
    public Guid? IconAssetId { get; set; }
    public string? DisplayFormat { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
}

/// <summary>
/// Internal model for wallet ownership and status.
/// Used by both CurrencyService and CurrencyAutogainTaskService.
/// </summary>
internal class WalletModel
{
    public Guid WalletId { get; set; }
    public Guid OwnerId { get; set; }
    public WalletOwnerType OwnerType { get; set; } = WalletOwnerType.Account;
    public Guid? RealmId { get; set; }
    public WalletStatus Status { get; set; } = WalletStatus.Active;
    public string? FrozenReason { get; set; }
    public DateTimeOffset? FrozenAt { get; set; }
    public Guid? FrozenBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
}

/// <summary>
/// Internal model for balance records.
/// Used by both CurrencyService and CurrencyAutogainTaskService.
/// </summary>
internal class BalanceModel
{
    public Guid WalletId { get; set; }
    public Guid CurrencyDefinitionId { get; set; }
    public double Amount { get; set; }
    public DateTimeOffset? LastAutogainAt { get; set; }
    public double DailyEarned { get; set; }
    public double WeeklyEarned { get; set; }
    public DateTimeOffset DailyResetAt { get; set; }
    public DateTimeOffset WeeklyResetAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
}

/// <summary>
/// Internal model for transaction records.
/// </summary>
internal class TransactionModel
{
    public Guid TransactionId { get; set; }
    public Guid? SourceWalletId { get; set; }
    public Guid? TargetWalletId { get; set; }
    public Guid CurrencyDefinitionId { get; set; }
    public double Amount { get; set; }
    public TransactionType TransactionType { get; set; } = TransactionType.Mint;
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public Guid? EscrowId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public double? SourceBalanceBefore { get; set; }
    public double? SourceBalanceAfter { get; set; }
    public double? TargetBalanceBefore { get; set; }
    public double? TargetBalanceAfter { get; set; }
    public JsonElement? Metadata { get; set; }
}

/// <summary>
/// Internal model for authorization holds.
/// </summary>
internal class HoldModel
{
    public Guid HoldId { get; set; }
    public Guid WalletId { get; set; }
    public Guid CurrencyDefinitionId { get; set; }
    public double Amount { get; set; }
    public HoldStatus Status { get; set; } = HoldStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public double? CapturedAmount { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
